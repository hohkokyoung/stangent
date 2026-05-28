#!/usr/bin/env python3
"""sqlite-vec retriever helper.

CLI:
  python retriever.py reindex
      Walk .claude/skills/<skill>/references/*.md for each enabled_skills,
      chunk, embed, and (re)write .claude/state/vectors.db.

  python retriever.py query "<text>" [k] [--skill <name> ...]
      Return JSON list of top-k chunks. Used by agentic_mcp.

Embedding provider: from .agentic.yml. Default voyage-3-lite. Offline
fallback: fastembed. v1: full re-embed each `reindex`; no hash cache.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sqlite3
import struct
import sys
from pathlib import Path
from typing import Iterable

try:
    import yaml  # type: ignore
except ImportError:
    yaml = None  # type: ignore

REPO_ROOT = Path.cwd().resolve()
CLAUDE_DIR = REPO_ROOT / ".claude"
AGENTIC_YML = CLAUDE_DIR / ".agentic.yml"
VECTORS_DB = CLAUDE_DIR / "state" / "vectors.db"
SKILLS_DIR = CLAUDE_DIR / "skills"

# ---------- config ----------

def load_config() -> dict:
    defaults = {
        "enabled_skills": [],
        "embedding": {"provider": "fastembed"},
        "retrieval": {"default_k": 6, "chunk_tokens": 400},
    }
    if not AGENTIC_YML.exists() or yaml is None:
        if yaml is None:
            sys.stderr.write("[retriever] PyYAML not installed; using defaults\n")
        return defaults
    cfg = yaml.safe_load(AGENTIC_YML.read_text(encoding="utf-8")) or defaults
    # back-compat: accept the older key name
    if "enabled_skills" not in cfg and "enabled_stacks" in cfg:
        cfg["enabled_skills"] = cfg.pop("enabled_stacks")
    return cfg


# ---------- chunking ----------

_HEADER_RE = re.compile(r"^(#{1,6})\s+(.*)$", re.MULTILINE)


def approx_tokens(text: str) -> int:
    # crude: words * 1.3
    return int(len(text.split()) * 1.3)


def chunk_markdown(text: str, target_tokens: int) -> list[tuple[str, str]]:
    """Return [(anchor, chunk_text)]. Anchor = nearest preceding heading or 'doc'."""
    lines = text.split("\n")
    chunks: list[tuple[str, str]] = []
    current: list[str] = []
    current_anchor = "doc"

    def flush():
        if not current:
            return
        body = "\n".join(current).strip()
        if body:
            chunks.append((current_anchor, body))

    for line in lines:
        m = _HEADER_RE.match(line)
        if m:
            # boundary on header
            flush()
            current = []
            current_anchor = m.group(2).strip().lower().replace(" ", "-")
            current.append(line)
            continue
        current.append(line)
        if approx_tokens("\n".join(current)) >= target_tokens:
            flush()
            current = []
    flush()
    return chunks


# ---------- embeddings ----------

def get_embedder(cfg: dict):
    provider = (cfg.get("embedding") or {}).get("provider", "voyage-3-lite")
    fallback = (cfg.get("embedding") or {}).get("fallback", "fastembed")

    if provider.startswith("voyage"):
        try:
            import voyageai  # type: ignore
            key = os.environ.get("VOYAGE_API_KEY")
            if not key:
                raise RuntimeError("VOYAGE_API_KEY unset")
            client = voyageai.Client(api_key=key)
            model = provider

            def embed(texts: list[str]) -> list[list[float]]:
                r = client.embed(texts, model=model, input_type="document")
                return r.embeddings

            return embed, "voyage:" + model
        except Exception as e:
            sys.stderr.write(f"[retriever] voyage unavailable ({e}); falling back to {fallback}\n")

    if fallback == "fastembed" or provider == "fastembed":
        from fastembed import TextEmbedding  # type: ignore
        model = TextEmbedding()  # default BAAI/bge-small-en-v1.5

        def embed(texts: list[str]) -> list[list[float]]:
            return [list(map(float, v)) for v in model.embed(texts)]

        return embed, "fastembed:default"

    raise RuntimeError(f"unknown embedding provider: {provider}")


def embed_query(cfg: dict, text: str) -> list[float]:
    provider = (cfg.get("embedding") or {}).get("provider", "voyage-3-lite")
    if provider.startswith("voyage"):
        try:
            import voyageai  # type: ignore
            key = os.environ.get("VOYAGE_API_KEY")
            if key:
                client = voyageai.Client(api_key=key)
                r = client.embed([text], model=provider, input_type="query")
                return r.embeddings[0]
        except Exception:
            pass
    from fastembed import TextEmbedding  # type: ignore
    return [float(x) for x in list(TextEmbedding().embed([text]))[0]]


# ---------- sqlite-vec storage ----------

def pack_f32(vec: list[float]) -> bytes:
    return struct.pack(f"{len(vec)}f", *vec)


def unpack_f32(buf: bytes) -> list[float]:
    n = len(buf) // 4
    return list(struct.unpack(f"{n}f", buf))


# sqlite3.Connection is a C extension type and (on some Python builds, incl.
# 3.13) refuses arbitrary attribute assignment. Track vec-loaded state in a
# module-level dict keyed by id(conn) instead.
_VEC_LOADED: dict[int, bool] = {}


def _vec_loaded(conn: sqlite3.Connection) -> bool:
    return _VEC_LOADED.get(id(conn), False)


def open_db() -> sqlite3.Connection:
    VECTORS_DB.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(VECTORS_DB)
    try:
        conn.enable_load_extension(True)
        import sqlite_vec  # type: ignore
        sqlite_vec.load(conn)
        conn.enable_load_extension(False)
        _VEC_LOADED[id(conn)] = True
    except Exception:
        # vec extension not available — we'll fall back to pure-python cosine
        _VEC_LOADED[id(conn)] = False
    return conn


def ensure_schema(conn: sqlite3.Connection, dim: int) -> None:
    conn.execute(
        """CREATE TABLE IF NOT EXISTS chunks(
            id INTEGER PRIMARY KEY,
            skill TEXT NOT NULL,
            file TEXT NOT NULL,
            anchor TEXT NOT NULL,
            text TEXT NOT NULL,
            embedding BLOB NOT NULL
        )"""
    )
    if _vec_loaded(conn):
        # virtual table; recreate to match dim
        conn.execute("DROP TABLE IF EXISTS vec_chunks")
        conn.execute(
            f"CREATE VIRTUAL TABLE vec_chunks USING vec0(embedding float[{dim}])"
        )
    conn.commit()


def cosine(a: list[float], b: list[float]) -> float:
    s = 0.0
    na = 0.0
    nb = 0.0
    for x, y in zip(a, b):
        s += x * y
        na += x * x
        nb += y * y
    if na == 0 or nb == 0:
        return 0.0
    return s / ((na ** 0.5) * (nb ** 0.5))


# ---------- commands ----------

def cmd_reindex() -> None:
    cfg = load_config()
    skills = cfg.get("enabled_skills") or []
    if not skills:
        print("[retriever] no enabled_skills in .agentic.yml; nothing to index")
        return
    target_tokens = (cfg.get("retrieval") or {}).get("chunk_tokens", 400)

    embed_fn, provider_id = get_embedder(cfg)

    if VECTORS_DB.exists():
        VECTORS_DB.unlink()
    conn = open_db()

    all_chunks: list[tuple[str, str, str, str]] = []  # skill, file, anchor, text
    for skill in skills:
        ref_dir = SKILLS_DIR / skill / "references"
        if not ref_dir.exists():
            print(f"[retriever] skip {skill}: no references dir")
            continue
        files = sorted(ref_dir.glob("*.md"))
        count = 0
        for md in files:
            text = md.read_text(encoding="utf-8")
            for anchor, chunk in chunk_markdown(text, target_tokens):
                all_chunks.append((skill, str(md.relative_to(REPO_ROOT)), anchor, chunk))
                count += 1
        print(f"[retriever] {skill}: {len(files)} files, {count} chunks")

    if not all_chunks:
        print("[retriever] no chunks; nothing to embed")
        return

    print(f"[retriever] embedding {len(all_chunks)} chunks via {provider_id}...")
    embeddings = embed_fn([c[3] for c in all_chunks])
    dim = len(embeddings[0])
    ensure_schema(conn, dim)

    cur = conn.cursor()
    for i, ((skill, file, anchor, text), emb) in enumerate(zip(all_chunks, embeddings), start=1):
        cur.execute(
            "INSERT INTO chunks(id, skill, file, anchor, text, embedding) VALUES (?, ?, ?, ?, ?, ?)",
            (i, skill, file, anchor, text, pack_f32(emb)),
        )
        if _vec_loaded(conn):
            cur.execute("INSERT INTO vec_chunks(rowid, embedding) VALUES (?, ?)", (i, pack_f32(emb)))
    conn.commit()
    print(f"[retriever] wrote {VECTORS_DB} (dim={dim})")


def cmd_query(text: str, k: int, skill_filter: list[str] | None) -> None:
    cfg = load_config()
    q_emb = embed_query(cfg, text)
    conn = open_db()

    where_sql = ""
    params: list = []
    if skill_filter:
        where_sql = "WHERE skill IN (" + ",".join("?" * len(skill_filter)) + ")"
        params = list(skill_filter)
    else:
        enabled = cfg.get("enabled_skills") or []
        if enabled:
            where_sql = "WHERE skill IN (" + ",".join("?" * len(enabled)) + ")"
            params = list(enabled)

    rows = conn.execute(
        f"SELECT id, skill, file, anchor, text, embedding FROM chunks {where_sql}",
        params,
    ).fetchall()

    scored = []
    for rid, s, f, a, t, blob in rows:
        emb = unpack_f32(blob)
        if len(emb) != len(q_emb):
            continue
        scored.append((cosine(q_emb, emb), {"file": f, "anchor": a, "text": t, "skill": s}))
    scored.sort(key=lambda x: x[0], reverse=True)
    out = [r for _, r in scored[:k]]
    print(json.dumps(out, ensure_ascii=False))


def main() -> None:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("reindex")
    q = sub.add_parser("query")
    q.add_argument("text")
    q.add_argument("k", nargs="?", type=int, default=6)
    q.add_argument("--skill", action="append", default=None,
                   help="Restrict to one or more skills; repeatable.")
    args = ap.parse_args()

    if args.cmd == "reindex":
        cmd_reindex()
    elif args.cmd == "query":
        cmd_query(args.text, args.k, args.skill)


if __name__ == "__main__":
    main()

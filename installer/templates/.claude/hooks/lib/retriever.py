#!/usr/bin/env python3
"""sqlite-vec retriever helper.

CLI:
  python retriever.py reindex
      (Re)index skill references and project source files into .claude/state/vectors.db.
      Skills: full rebuild each run. Project files: incremental hash-cached.

  python retriever.py query "<text>" [k] [--skill <name> ...]
      Return JSON list of top-k chunks. Used by agentic_mcp.

Embedding provider: from .agentic.yml. Default voyage-3-lite. Offline
fallback: fastembed.
"""
from __future__ import annotations

import argparse
import datetime
import fnmatch
import hashlib
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
PROJECT_YML = CLAUDE_DIR / "state" / "project.yml"

_DEFAULT_PROJECT_EXCLUDES = [
    # VCS / tooling
    ".git/**", ".claude/**",
    # JS / Node
    "node_modules/**", "dist/**", "build/**", ".next/**", ".nuxt/**",
    "*.min.js", "*.min.css",
    # Python
    "__pycache__/**", ".venv/**", "venv/**", ".tox/**", "*.egg-info/**",
    # Rust
    "target/**",
    # Go
    "vendor/**",
    # Ruby
    ".bundle/**",
    # iOS / macOS (Swift/ObjC)
    "Pods/**", ".build/**", "DerivedData/**",
    # Dart / Flutter
    ".dart_tool/**", ".pub-cache/**",
    # Java / Kotlin (non-Android)
    ".gradle/**", ".idea/**",
    # .NET
    "obj/**", "bin/**",
    # Elixir
    "_build/**", ".elixir_ls/**",
    # General generated / lock files
    "*.lock",
    # ---- Generated source code (machine-written; noise for retrieval) ----
    # These live *alongside* hand-written code, so directory excludes above
    # don't catch them — they must be matched by filename suffix.
    # Dart / Flutter codegen: build_runner, freezed, json_serializable, riverpod
    "*.g.dart", "*.freezed.dart", "*.gr.dart", "*.config.dart", "*.mocks.dart",
    # Protobuf / gRPC (all languages)
    "*.pb.dart", "*.pbenum.dart", "*.pbjson.dart", "*.pbserver.dart",
    "*.pb.go", "*_pb2.py", "*_pb2_grpc.py", "*.pb.cc", "*.pb.h",
    # TypeScript declaration files + source maps
    "*.d.ts", "*.map",
    # C# designer / generated partials (case varies by tooling)
    "*.designer.cs", "*.Designer.cs", "*.g.cs", "*.g.i.cs",
    # Jest / snapshot fixtures
    "*.snap",
    # Common codegen naming convention (e.g. schema.generated.ts)
    "*.generated.*",
    # Test files (excluded so source retrieval isn't polluted by test fixtures)
    "tests/**", "__tests__/**", "*.test.*", "*.spec.*",
]

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


# ---------- source code chunking ----------

_BLANK_BLOCK_RE = re.compile(r"\n\s*\n")


def build_file_preamble(rel_path: str) -> str:
    ext = Path(rel_path).suffix.lstrip(".")
    return f"# file: {rel_path}\n# lang: {ext}\n"


def chunk_source_code(text: str, target_tokens: int) -> list[tuple[str, str]]:
    """Return [(anchor, chunk_text)]. Anchor = first non-empty line (definition header)."""
    blocks = _BLANK_BLOCK_RE.split(text)
    chunks: list[tuple[str, str]] = []
    word_limit = max(1, int(target_tokens / 1.3))

    for block in blocks:
        body = block.strip()
        if not body:
            continue
        anchor = next((line.strip() for line in body.split("\n") if line.strip()), "doc")
        if len(anchor) > 120:
            anchor = anchor[:120]

        if approx_tokens(body) <= target_tokens:
            chunks.append((anchor, body))
        else:
            words = body.split()
            for i in range(0, len(words), word_limit):
                part = " ".join(words[i : i + word_limit])
                part_anchor = anchor if i == 0 else f"{anchor} (cont.)"
                chunks.append((part_anchor, part))

    return chunks


# ---------- project glob helpers ----------

def _match_glob(rel_path: str, pat: str) -> bool:
    if fnmatch.fnmatch(rel_path, pat):
        return True
    # Directory prefix: "foo/**" excludes everything under foo/.
    if pat.endswith("/**") and rel_path.startswith(pat[:-3] + "/"):
        return True
    return False


def _is_excluded(rel_path: str, excludes: list[str]) -> bool:
    """True if rel_path is excluded.

    Patterns are evaluated in order; the last matching pattern wins. A pattern
    prefixed with "!" re-includes (un-excludes) a path an earlier pattern caught
    — e.g. defaults exclude "tests/**", and a project can add "!tests/**" to
    index its tests anyway. This ordering is why config excludes are appended
    *after* the built-in defaults in _load_project_globs.
    """
    excluded = False
    for pat in excludes:
        if pat.startswith("!"):
            if _match_glob(rel_path, pat[1:]):
                excluded = False
        elif _match_glob(rel_path, pat):
            excluded = True
    return excluded


def _load_project_globs(cfg: dict) -> tuple[list[str], list[str]]:
    pi = cfg.get("project_index") or {}
    include = [g for g in (pi.get("include") or []) if g]

    if not include and yaml is not None and PROJECT_YML.exists():
        try:
            pdata = yaml.safe_load(PROJECT_YML.read_text(encoding="utf-8")) or {}
            include = pdata.get("project_index_globs") or []
        except Exception:
            pass

    # Config excludes are ADDITIVE: built-in defaults always apply, and any
    # patterns from .agentic.yml are appended after them. This means adding one
    # project-specific exclude never silently drops the sane defaults (build
    # dirs, vendored deps, generated code). To un-exclude a default, prefix a
    # config pattern with "!" (handled in _is_excluded).
    user_exclude = [g for g in (pi.get("exclude") or []) if g]
    exclude = _DEFAULT_PROJECT_EXCLUDES + user_exclude
    return include, exclude


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
    _ensure_base_schema(conn)
    if _vec_loaded(conn):
        conn.execute("DROP TABLE IF EXISTS vec_chunks")
        conn.execute(f"CREATE VIRTUAL TABLE vec_chunks USING vec0(embedding float[{dim}] distance=cosine)")
    conn.commit()


def _ensure_base_schema(conn: sqlite3.Connection) -> None:
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
    conn.execute(
        """CREATE TABLE IF NOT EXISTS file_hashes(
            path TEXT PRIMARY KEY,
            hash TEXT NOT NULL,
            indexed_at TEXT NOT NULL
        )"""
    )
    conn.commit()


def _rebuild_vec_chunks(conn: sqlite3.Connection) -> None:
    if not _vec_loaded(conn):
        return
    row = conn.execute("SELECT embedding FROM chunks LIMIT 1").fetchone()
    if not row:
        return
    dim = len(unpack_f32(row[0]))
    conn.execute("DROP TABLE IF EXISTS vec_chunks")
    conn.execute(f"CREATE VIRTUAL TABLE vec_chunks USING vec0(embedding float[{dim}] distance=cosine)")
    conn.execute("INSERT INTO vec_chunks(rowid, embedding) SELECT id, embedding FROM chunks")
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

def _reindex_project(
    conn: sqlite3.Connection,
    cfg: dict,
    embed_fn,
    provider_id: str,
    target_tokens: int,
) -> None:
    include_globs, exclude_globs = _load_project_globs(cfg)
    if not include_globs:
        print("[retriever] no project globs configured — skipping project indexing.")
        print("[retriever]   Set project_index.include in .agentic.yml or run /agentic-index for auto-detection.")
        return

    candidate: set[Path] = set()
    for pattern in include_globs:
        candidate.update(REPO_ROOT.glob(pattern))

    project_files = sorted(
        f for f in candidate
        if f.is_file() and not _is_excluded(str(f.relative_to(REPO_ROOT)), exclude_globs)
    )

    existing_hashes: dict[str, str] = dict(
        conn.execute("SELECT path, hash FROM file_hashes").fetchall()
    )
    first_run = not existing_hashes
    if first_run and project_files:
        print(f"[retriever] first run — indexing {len(project_files)} project files (this may take a moment)")

    cur = conn.cursor()
    n_indexed = n_skipped = n_encoding = 0

    for fpath in project_files:
        rel = str(fpath.relative_to(REPO_ROOT))
        try:
            text = fpath.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            print(f"[retriever] skip {rel}: not UTF-8")
            n_encoding += 1
            continue

        file_hash = hashlib.sha256(text.encode("utf-8")).hexdigest()
        if existing_hashes.get(rel) == file_hash:
            n_skipped += 1
            continue

        chunks = chunk_source_code(text, target_tokens)
        if not chunks:
            continue

        preamble = build_file_preamble(rel)
        embeddings = embed_fn([preamble + chunk_text for _, chunk_text in chunks])

        cur.execute("DELETE FROM chunks WHERE skill='project' AND file=?", (rel,))
        for (anchor, chunk_text), emb in zip(chunks, embeddings):
            cur.execute(
                "INSERT INTO chunks(skill, file, anchor, text, embedding) VALUES (?,?,?,?,?)",
                ("project", rel, anchor, chunk_text, pack_f32(emb)),
            )

        now = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")
        cur.execute(
            "INSERT OR REPLACE INTO file_hashes(path, hash, indexed_at) VALUES (?,?,?)",
            (rel, file_hash, now),
        )
        n_indexed += 1

    # Stale cleanup: paths in file_hashes that no longer exist on disk
    current_paths = {str(f.relative_to(REPO_ROOT)) for f in project_files}
    n_removed = 0
    for stale_path in list(existing_hashes.keys()):
        if stale_path not in current_paths:
            cur.execute("DELETE FROM chunks WHERE skill='project' AND file=?", (stale_path,))
            cur.execute("DELETE FROM file_hashes WHERE path=?", (stale_path,))
            n_removed += 1

    conn.commit()
    print(
        f"[retriever] project: {n_indexed} indexed, {n_skipped} skipped (unchanged), "
        f"{n_removed} removed (stale), {n_encoding} skipped (non-UTF-8)"
    )


def detect_stack() -> None:
    """Detect project stack and write test_framework + project_index_globs to project.yml."""
    root = REPO_ROOT
    framework = "unknown"
    globs: list[str] = []

    # Scan root + immediate subdirectories so monorepos are detected
    # (e.g. Flutter in mobile/ + FastAPI in backend/).
    scan_dirs = [root] + [d for d in root.iterdir() if d.is_dir() and not d.name.startswith(".")]

    def has_file(name: str) -> bool:
        return any((d / name).exists() for d in scan_dirs)

    def has_dir(name: str) -> bool:
        return any((d / name).is_dir() for d in scan_dirs)

    def has_glob(pattern: str) -> bool:
        return any(any(d.glob(pattern)) for d in scan_dirs)

    has_pubspec = has_file("pubspec.yaml")
    has_android = has_dir("android")
    has_ios = has_dir("ios")
    has_go_mod = has_file("go.mod")
    has_cargo = has_file("Cargo.toml")
    has_gemfile = has_file("Gemfile")
    has_pom = has_file("pom.xml")
    has_gradle = has_glob("build.gradle") or has_glob("build.gradle.kts")
    has_csproj = has_glob("*.csproj") or has_glob("*.sln")
    has_mix = has_file("mix.exs")
    has_composer = has_file("composer.json")
    has_requirements = has_file("requirements.txt")
    has_pyproject = has_file("pyproject.toml")

    is_mobile = False
    is_web_frontend = False

    if has_pubspec:
        is_mobile = True
        globs += ["**/*.dart"]
    if (has_android or has_ios) and not has_pubspec:
        is_mobile = True
        globs += ["**/*.kt", "**/*.swift"]

    pkg_json = next((d / "package.json" for d in scan_dirs if (d / "package.json").exists()), None)
    if pkg_json is not None:
        try:
            pkg = json.loads(pkg_json.read_text(encoding="utf-8"))
            deps = set((pkg.get("dependencies") or {}) | (pkg.get("devDependencies") or {}))
            browser_frameworks = {"next", "react", "vue", "svelte", "nuxt", "angular", "vite"}
            if deps & browser_frameworks:
                is_web_frontend = True
                globs += ["**/*.ts", "**/*.tsx", "**/*.js", "**/*.jsx"]
            else:
                globs += ["**/*.ts", "**/*.js"]
        except Exception:
            globs += ["**/*.ts", "**/*.js"]

    if has_requirements or has_pyproject:
        globs += ["**/*.py"]
        if framework == "unknown" and not is_mobile and not is_web_frontend:
            framework = "pytest"
    if has_go_mod:
        globs += ["**/*.go"]
        if framework == "unknown" and not is_mobile and not is_web_frontend:
            framework = "go_test"
    if has_cargo:
        globs += ["**/*.rs"]
        if framework == "unknown" and not is_mobile and not is_web_frontend:
            framework = "cargo_test"
    if has_gemfile:
        globs += ["**/*.rb"]
        if framework == "unknown" and not is_mobile and not is_web_frontend:
            framework = "rspec"
    if (has_pom or has_gradle) and not has_android:
        globs += ["**/*.java", "**/*.kt"]
        if framework == "unknown" and not is_mobile and not is_web_frontend:
            framework = "junit"
    if has_csproj:
        globs += ["**/*.cs"]
        if framework == "unknown" and not is_mobile and not is_web_frontend:
            framework = "dotnet_test"
    if has_mix:
        globs += ["**/*.ex", "**/*.exs"]
        if framework == "unknown" and not is_mobile and not is_web_frontend:
            framework = "ex_unit"
    if has_composer:
        globs += ["**/*.php"]

    if is_mobile:
        framework = "maestro"
    elif is_web_frontend:
        framework = "playwright"

    globs = list(dict.fromkeys(globs))  # deduplicate, preserve order

    # Merge into existing project.yml (preserve unknown extra fields)
    existing: dict = {}
    if yaml is not None and PROJECT_YML.exists():
        try:
            existing = yaml.safe_load(PROJECT_YML.read_text(encoding="utf-8")) or {}
        except Exception:
            pass

    existing["test_framework"] = framework
    existing["detected_at"] = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")
    existing["project_index_globs"] = globs

    PROJECT_YML.parent.mkdir(parents=True, exist_ok=True)
    if yaml is not None:
        PROJECT_YML.write_text(yaml.dump(existing, default_flow_style=False, allow_unicode=True), encoding="utf-8")
    else:
        # yaml not available — write minimal YAML by hand
        lines = [
            f"test_framework: {framework}",
            f"detected_at: '{existing['detected_at']}'",
            "project_index_globs:",
        ] + ([f"  - '{g}'" for g in globs] if globs else ["  []"])
        PROJECT_YML.write_text("\n".join(lines) + "\n", encoding="utf-8")

    if globs:
        print(f"[agentic-index] detected test_framework: {framework}")
        print(f"[agentic-index] project_index_globs: {globs}")
    else:
        print("[agentic-index] could not detect stack. Set test_framework and project_index_globs manually in .claude/state/project.yml")


def cmd_reindex(project_only: bool = False) -> None:
    cfg = load_config()
    target_tokens = (cfg.get("retrieval") or {}).get("chunk_tokens", 400)
    embed_fn, provider_id = get_embedder(cfg)

    conn = open_db()
    _ensure_base_schema(conn)

    if not project_only:
        detect_stack()

    if not project_only:
        # Skills pass: full rebuild (preserve project chunks)
        conn.execute("DELETE FROM chunks WHERE skill != 'project'")
        conn.commit()

        skills = cfg.get("enabled_skills") or []
        all_skill_chunks: list[tuple[str, str, str, str]] = []  # skill, file, anchor, text
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
                    all_skill_chunks.append((skill, str(md.relative_to(REPO_ROOT)), anchor, chunk))
                    count += 1
            print(f"[retriever] {skill}: {len(files)} files, {count} chunks")

        if not skills:
            print("[retriever] no enabled_skills in .agentic.yml; skipping skills pass")

        if all_skill_chunks:
            print(f"[retriever] embedding {len(all_skill_chunks)} skill chunks via {provider_id}...")
            embeddings = embed_fn([c[3] for c in all_skill_chunks])
            cur = conn.cursor()
            for (skill, file, anchor, text), emb in zip(all_skill_chunks, embeddings):
                cur.execute(
                    "INSERT INTO chunks(skill, file, anchor, text, embedding) VALUES (?,?,?,?,?)",
                    (skill, file, anchor, text, pack_f32(emb)),
                )
            conn.commit()

    # Project pass: incremental, hash-cached
    _reindex_project(conn, cfg, embed_fn, provider_id, target_tokens)

    # Rebuild ANN index from all current chunks
    _rebuild_vec_chunks(conn)

    total = conn.execute("SELECT COUNT(*) FROM chunks").fetchone()[0]
    print(f"[retriever] total: {total} chunks in {VECTORS_DB}")


def _resolve_scope(cfg: dict, skill_filter: list[str] | None) -> list[str]:
    """Skills the query is restricted to (empty list = no restriction)."""
    if skill_filter:
        return list(skill_filter)
    return list(cfg.get("enabled_skills") or [])


def _fetch_meta(conn: sqlite3.Connection, ids: list[int], scope: list[str]) -> dict[int, dict]:
    if not ids:
        return {}
    sql = "SELECT id, skill, file, anchor, text FROM chunks WHERE id IN (" + ",".join("?" * len(ids)) + ")"
    params: list = list(ids)
    if scope:
        sql += " AND skill IN (" + ",".join("?" * len(scope)) + ")"
        params += scope
    out = {}
    for rid, s, f, a, t in conn.execute(sql, params).fetchall():
        out[rid] = {"file": f, "anchor": a, "text": t, "skill": s}
    return out


def _knn_query(conn: sqlite3.Connection, q_emb: list[float], k: int, scope: list[str]) -> list[dict] | None:
    """Top-k via the sqlite-vec ANN index. Returns None (→ brute-force fallback)
    if the extension is unavailable, the query errors, or scope filtering thinned
    the ANN candidates below k without having scanned the whole table."""
    if not _vec_loaded(conn):
        return None
    try:
        total = conn.execute("SELECT COUNT(*) FROM vec_chunks").fetchone()[0]
        if total == 0:
            return None
        # Over-fetch when scoped, since the vec table has no skill column and we
        # filter by skill after the KNN.
        over = k if not scope else min(total, max(k * 8, 64))
        knn = conn.execute(
            "SELECT rowid FROM vec_chunks WHERE embedding MATCH ? ORDER BY distance LIMIT ?",
            (pack_f32(q_emb), over),
        ).fetchall()
    except Exception:
        return None
    ordered_ids = [rid for (rid,) in knn]
    meta = _fetch_meta(conn, ordered_ids, scope)
    result = [meta[i] for i in ordered_ids if i in meta][:k]
    if scope and len(result) < k and over < total:
        return None  # may have missed scoped chunks — let brute force be exhaustive
    return result


def _brute_query(conn: sqlite3.Connection, q_emb: list[float], k: int, scope: list[str]) -> list[dict]:
    where_sql = ""
    params: list = []
    if scope:
        where_sql = "WHERE skill IN (" + ",".join("?" * len(scope)) + ")"
        params = list(scope)
    rows = conn.execute(
        f"SELECT skill, file, anchor, text, embedding FROM chunks {where_sql}", params
    ).fetchall()
    scored, considered, dim_mismatch = [], 0, 0
    for s, f, a, t, blob in rows:
        considered += 1
        emb = unpack_f32(blob)
        if len(emb) != len(q_emb):
            dim_mismatch += 1
            continue
        scored.append((cosine(q_emb, emb), {"file": f, "anchor": a, "text": t, "skill": s}))
    # Surface the silent-empty case: every candidate was dropped on dimension.
    # The usual cause is a different embedding provider at index vs query time.
    if considered and dim_mismatch == considered:
        sys.stderr.write(
            f"[retriever] all {considered} chunks skipped on embedding-dimension mismatch "
            f"(query dim {len(q_emb)}). The index was likely built with a different "
            f"embedding provider than the current one — re-run /agentic-index.\n"
        )
    scored.sort(key=lambda x: x[0], reverse=True)
    return [r for _, r in scored[:k]]


def cmd_query(text: str, k: int, skill_filter: list[str] | None) -> None:
    cfg = load_config()
    q_emb = embed_query(cfg, text)
    conn = open_db()
    scope = _resolve_scope(cfg, skill_filter)
    out = _knn_query(conn, q_emb, k, scope)
    if out is None:
        out = _brute_query(conn, q_emb, k, scope)
    print(json.dumps(out, ensure_ascii=False))


def main() -> None:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    ri = sub.add_parser("reindex")
    ri.add_argument("--project-only", action="store_true",
                    help="Only re-index project files (skip skills re-embed).")
    q = sub.add_parser("query")
    q.add_argument("text")
    q.add_argument("k", nargs="?", type=int, default=6)
    q.add_argument("--skill", action="append", default=None,
                   help="Restrict to one or more skills; repeatable.")
    args = ap.parse_args()

    if args.cmd == "reindex":
        cmd_reindex(project_only=getattr(args, "project_only", False))
    elif args.cmd == "query":
        cmd_query(args.text, args.k, args.skill)


if __name__ == "__main__":
    main()

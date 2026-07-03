#!/usr/bin/env python3
"""Deterministic dispatch planner for /agentic-build.

Turns a run directory of task files into an ordered, fully-resolved dispatch
plan: topological order, currently-runnable set, and the model / skills / k
resolved per task. This is the logic that used to live as prose in
`agentic-build.md` (topo sort, cycle detection, runnable filtering, complexity
routing). It is a PURE FUNCTION of the task files + `.agentic.yml`, so it is
unit-tested and cannot drift the way a re-derived prose algorithm can.

Usage:
    dispatch_plan.py <run_id> [--task <task-id>] [--session-model <model>]

Emits JSON on stdout:
    {
      "run_id": "...",
      "cycle": false,
      "order": ["s2", "t1", "t2"],          # full topological order (ids)
      "runnable": [ <resolved task>, ... ],  # status==pending & deps all done
      "blocked_by_dep": ["t3"]               # pending, but a dep is blocked
    }

A resolved task is:
    {"task_id","role","complexity","model","role_baseline","routing_applied",
     "skills","k"}

Exit codes: 0 ok · 2 usage/parse error · 3 dependency cycle · 4 --task refused.
The command layer only executes what this script emits; it does not re-derive
ordering or routing.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    import yaml  # type: ignore
except ImportError:
    yaml = None  # type: ignore

REPO_ROOT = Path.cwd().resolve()
AGENTIC_YML = REPO_ROOT / ".claude" / ".agentic.yml"
PLANS_DIR = REPO_ROOT / ".claude" / "state" / "plans"

# Faithful defaults from the old agentic-build.md contract. Overridable via
# .agentic.yml so newer models can be ranked without editing this file.
DEFAULT_CAPABILITY_ORDER = [
    "claude-haiku-4-5-20251001",
    "claude-sonnet-4-6",
    "claude-opus-4-8",
]
DEFAULT_LOW_CAP = "claude-haiku-4-5-20251001"
DEFAULT_HIGH_FLOOR = "claude-sonnet-4-6"
# Model an unranked id is treated as, for comparison purposes.
COMPARISON_FALLBACK = "claude-sonnet-4-6"
ROLES = ("planner", "sketcher", "implementer", "reviewer", "tester", "debugger", "refactor")


# ---------- config ----------

def load_config() -> dict:
    if not AGENTIC_YML.exists() or yaml is None:
        return {}
    try:
        return yaml.safe_load(AGENTIC_YML.read_text(encoding="utf-8")) or {}
    except Exception:
        return {}


# ---------- frontmatter parsing ----------

def _parse_scalar(v: str):
    v = v.strip()
    if (v.startswith('"') and v.endswith('"')) or (v.startswith("'") and v.endswith("'")):
        return v[1:-1]
    if v in ("null", "~", ""):
        return None
    return v


def _parse_flow_list(v: str) -> list[str]:
    inner = v.strip()[1:-1].strip()  # drop [ ]
    if not inner:
        return []
    out = []
    for item in inner.split(","):
        s = _parse_scalar(item)
        if s is not None and s != "":
            out.append(s)
    return out


def _strip_comment(line: str) -> str:
    """Drop a trailing `# comment` from a YAML line, but never a `#` that sits
    inside a quoted string or is not preceded by whitespace (e.g. `"fix #42"`)."""
    in_s = in_d = False
    for idx, ch in enumerate(line):
        if ch == "'" and not in_d:
            in_s = not in_s
        elif ch == '"' and not in_s:
            in_d = not in_d
        elif ch == "#" and not in_s and not in_d and (idx == 0 or line[idx - 1].isspace()):
            return line[:idx]
    return line


def parse_frontmatter(text: str) -> dict:
    """Extract the YAML frontmatter block into a dict.

    Uses PyYAML when available; otherwise a minimal parser covering the shapes
    the planner emits (scalars, flow lists `[a, b]`, and block lists of `-`).
    """
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        return {}
    end = None
    for i in range(1, len(lines)):
        if lines[i].strip() == "---":
            end = i
            break
    if end is None:
        return {}
    block = "\n".join(lines[1:end])

    if yaml is not None:
        try:
            data = yaml.safe_load(block)
            return data if isinstance(data, dict) else {}
        except Exception:
            pass  # fall through to minimal parser

    data: dict = {}
    fm = lines[1:end]
    i = 0
    while i < len(fm):
        raw = fm[i]
        line = _strip_comment(raw).rstrip()
        if not line.strip() or ":" not in line:
            i += 1
            continue
        key, _, val = line.partition(":")
        key = key.strip()
        val = val.strip()
        if val.startswith("["):
            data[key] = _parse_flow_list(val)
        elif val == "" and i + 1 < len(fm) and fm[i + 1].lstrip().startswith("- "):
            items = []
            j = i + 1
            while j < len(fm) and fm[j].lstrip().startswith("- "):
                items.append(_parse_scalar(fm[j].lstrip()[2:]))
                j += 1
            data[key] = [x for x in items if x is not None]
            i = j
            continue
        else:
            data[key] = _parse_scalar(val)
        i += 1
    return data


def _as_list(v) -> list[str]:
    if v is None:
        return []
    if isinstance(v, list):
        return [str(x) for x in v]
    return [str(v)]


def load_tasks(run_dir: Path) -> list[dict]:
    tasks = []
    for f in sorted(run_dir.glob("*.md")):
        if f.name == "_overview.md":
            continue
        fm = parse_frontmatter(f.read_text(encoding="utf-8"))
        tid = str(fm.get("id") or f.stem)
        k_raw = fm.get("k")
        try:
            k_val = int(k_raw) if k_raw not in (None, "", "null") else None
        except (TypeError, ValueError):
            k_val = None
        tasks.append({
            "id": tid,
            "path": str(f),
            "role": str(fm.get("role") or ""),
            "status": str(fm.get("status") or "pending"),
            "complexity": (str(fm.get("complexity")).lower()
                           if fm.get("complexity") else "medium"),
            "k": k_val,
            "depends_on": _as_list(fm.get("depends_on")),
            "skills_to_load": _as_list(fm.get("skills_to_load")),
        })
    return tasks


# ---------- topological sort ----------

def topo_sort(tasks: list[dict]) -> tuple[list[str], list[str] | None]:
    """Return (order, cycle_path). cycle_path is None when acyclic."""
    by_id = {t["id"] for t in tasks}
    deps = {t["id"]: [d for d in t["depends_on"] if d in by_id] for t in tasks}
    # Kahn with deterministic tie-break by id keeps output stable.
    WHITE, GRAY, BLACK = 0, 1, 2
    color = {tid: WHITE for tid in deps}
    order: list[str] = []
    stack_path: list[str] = []

    def visit(node: str) -> list[str] | None:
        color[node] = GRAY
        stack_path.append(node)
        for dep in sorted(deps[node]):
            if color[dep] == GRAY:
                idx = stack_path.index(dep)
                return stack_path[idx:] + [dep]
            if color[dep] == WHITE:
                cyc = visit(dep)
                if cyc:
                    return cyc
        color[node] = BLACK
        stack_path.pop()
        order.append(node)
        return None

    for tid in sorted(deps):
        if color[tid] == WHITE:
            cyc = visit(tid)
            if cyc:
                return [], cyc
    return order, None


# ---------- model routing ----------

def _rank_fn(order: list[str]):
    fallback = order.index(COMPARISON_FALLBACK) if COMPARISON_FALLBACK in order else len(order) // 2

    def rank(m: str) -> int:
        return order.index(m) if m in order else fallback

    return rank


def resolve_model(role: str, complexity: str, cfg: dict, session_model: str | None) -> tuple[str, str, bool]:
    """Return (selected_model, role_baseline, routing_applied)."""
    models = cfg.get("models") or {}
    role_model = models.get(role) or models.get("default") or session_model or COMPARISON_FALLBACK

    routing = cfg.get("complexity_routing") or {}
    enabled = bool(routing.get("enabled", False))
    order = cfg.get("model_capability_order") or DEFAULT_CAPABILITY_ORDER
    low_cap = routing.get("low_cap") or DEFAULT_LOW_CAP
    high_floor = routing.get("high_floor") or DEFAULT_HIGH_FLOOR
    rank = _rank_fn(order)

    selected = role_model
    if enabled:
        if complexity == "low":
            selected = role_model if rank(role_model) <= rank(low_cap) else low_cap
        elif complexity == "high":
            selected = role_model if rank(role_model) >= rank(high_floor) else high_floor
        # medium → unchanged
    return selected, role_model, selected != role_model


def resolve_k(role: str, task_k, cfg: dict) -> int:
    retrieval = cfg.get("retrieval") or {}
    role_k = retrieval.get("role_k") or {}
    if role in role_k:
        try:
            return int(role_k[role])
        except (TypeError, ValueError):
            pass
    if task_k is not None:
        return int(task_k)
    try:
        return int(retrieval.get("default_k", 6))
    except (TypeError, ValueError):
        return 6


def resolve_skills(role: str, skills: list[str], cfg: dict) -> list[str]:
    if role != "tester":
        return skills
    test_group = ((cfg.get("skill_groups") or {}).get("test")) or []
    if not test_group:
        return skills
    return [s for s in skills if s in test_group]


def resolve_task(t: dict, cfg: dict, session_model: str | None) -> dict:
    complexity = t["complexity"] if t["complexity"] in ("low", "medium", "high") else "medium"
    model, baseline, applied = resolve_model(t["role"], complexity, cfg, session_model)
    return {
        "task_id": t["id"],
        "path": t["path"],
        "role": t["role"],
        "complexity": complexity,
        "model": model,
        "role_baseline": baseline,
        "routing_applied": applied,
        "skills": resolve_skills(t["role"], t["skills_to_load"], cfg),
        "k": resolve_k(t["role"], t["k"], cfg),
    }


# ---------- plan assembly ----------

def build_plan(run_id: str, cfg: dict, only_task: str | None, session_model: str | None) -> tuple[dict, int]:
    run_dir = PLANS_DIR / run_id
    if not run_dir.is_dir():
        return {"error": f"run dir not found: {run_dir}"}, 2
    tasks = load_tasks(run_dir)
    if not tasks:
        return {"error": f"no task files in {run_dir}"}, 2

    order, cycle = topo_sort(tasks)
    if cycle:
        return {"run_id": run_id, "cycle": True,
                "error": "dependency cycle: " + " -> ".join(cycle)}, 3

    by_id = {t["id"]: t for t in tasks}
    status = {t["id"]: t["status"] for t in tasks}

    def has_dangling(t: dict) -> bool:
        return any(d not in by_id for d in t["depends_on"])

    def deps_done(t: dict) -> bool:
        # A dep that names no existing task is NOT satisfied — it keeps the task
        # out of the runnable set (and shows up in invalid_deps) rather than
        # being silently dropped, which would dispatch the task out of order.
        return all(status.get(d) == "done" for d in t["depends_on"])

    def dep_blocked(t: dict) -> bool:
        return any(status.get(d) == "blocked" for d in t["depends_on"] if d in by_id)

    runnable, blocked_by_dep, invalid_deps = [], [], []
    for tid in order:
        t = by_id[tid]
        if t["status"] != "pending":
            continue
        if has_dangling(t):
            missing = [d for d in t["depends_on"] if d not in by_id]
            invalid_deps.append({"task_id": tid, "missing": missing})
            continue
        if deps_done(t):
            runnable.append(resolve_task(t, cfg, session_model))
        elif dep_blocked(t):
            blocked_by_dep.append(tid)

    if only_task is not None:
        if only_task not in by_id:
            return {"run_id": run_id, "error": f"task not found: {only_task}"}, 4
        match = [r for r in runnable if r["task_id"] == only_task]
        if not match:
            reason = ("already " + status[only_task]) if status[only_task] != "pending" \
                else "dependencies not all done"
            return {"run_id": run_id, "error": f"task {only_task} not runnable: {reason}"}, 4
        runnable = match

    return {"run_id": run_id, "cycle": False, "order": order,
            "runnable": runnable, "blocked_by_dep": blocked_by_dep,
            "invalid_deps": invalid_deps}, 0


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("run_id")
    ap.add_argument("--task", default=None)
    ap.add_argument("--session-model", default=None)
    args = ap.parse_args()

    plan, code = build_plan(args.run_id, load_config(), args.task, args.session_model)
    print(json.dumps(plan, ensure_ascii=False, indent=2))
    sys.exit(code)


if __name__ == "__main__":
    main()

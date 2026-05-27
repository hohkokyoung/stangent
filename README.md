# Agentic Development Workflow System

A Claude Code–native agentic development workflow. Installs per-project under `.claude/`. Agents are organized by **role** (planner / implementer / reviewer / tester), not by stack. Stack expertise lives in skill prompt blocks plus a retrievable references corpus.

> v1 — correctness of the loop. v2 / v3 are symptom-driven; nothing built without a named failure mode.

---

## Install into a project

```bash
python <repo>/installer/agentic.py --target /path/to/project
```

Or from inside the target:
```bash
cd /path/to/project
python <repo>/installer/agentic.py
```

Cross-platform (Windows / macOS / Linux). Idempotent — re-run anytime to refresh templates; user-added files are preserved.

Runtime dependencies in the target project:
```bash
pip install pyyaml fastembed sqlite-vec
# optional: pip install voyageai && export VOYAGE_API_KEY=...   (better embeddings)
```

## Uninstall

```bash
python <repo>/installer/agentic.py --target /path/to/project --uninstall
```
Removes only `_agentic_managed` hooks/MCP entries from `settings.json`, system-owned directories, and the `# >>> agentic` block from `.gitignore`. Anything you added is left alone.

---

## Per-feature workflow

In the installed project, in Claude Code:

```
/agentic-index                              # one-time (or when references change)
/agentic-plan <natural-language goal>       # planner emits FEAT-### with task files
/agentic-build all                          # dispatcher runs in dep order
/agentic-status                             # dashboard
/agentic-update-plan <run-id> <amendment>   # amend without touching done tasks
```

The planner is strict — it walks an 11-dimension coverage checklist (scope, functional, acceptance, edges, auth, validation, error UX, data model, API, NFRs, out-of-scope) and asks via `AskUserQuestion` on blocking gaps, up to 4 rounds.

---

## Layout in an installed project

```
.claude/
├── .agentic.yml                # enabled_skills, embedding, deny patterns, plan_id format
├── settings.json               # hooks + MCP servers (all _agentic_managed: true)
├── agents/
│   ├── planner.md              # decomposition only — no file names, no classes
│   ├── implementer.md          # one task; loads skills verbatim; one retrieve() call
│   ├── reviewer.md             # append-only ## Review; never finalizes done
│   └── tester.md               # finalizes done / blocked
├── commands/
│   ├── agentic-plan.md
│   ├── agentic-build.md        # fixed topo-sort dispatcher contract
│   ├── agentic-status.md
│   ├── agentic-index.md
│   └── agentic-update-plan.md
├── skills/
│   ├── fastapi/   SKILL.md + references/*.md
│   ├── flutter/   SKILL.md + references/*.md   # Riverpod 3.x
│   └── supabase/  SKILL.md + references/*.md
├── hooks/
│   ├── pre_tool_use.py         # hard safety only (rm -rf, force push, DROP, TRUNCATE, ...)
│   ├── post_tool_use.py        # JSONL logger, one file per run_id
│   └── lib/
│       ├── retriever.py        # sqlite-vec + voyage/fastembed
│       └── plan_id.py          # FEAT-### allocator
├── mcp/
│   └── agentic_mcp.py          # exposes retrieve(query, k, skills) over stdio MCP
└── state/                      # gitignored
    ├── plans/<FEAT-###>/
    │   ├── _overview.md
    │   └── t1.md, t2.md, ...
    ├── vectors.db
    └── logs/<FEAT-###>.jsonl
```

---

## Core invariants (v1)

- **1 task = 1 file.** The task file is the single source of truth.
- **State machine:** `pending → running → done | blocked`. Terminal states are terminal; no auto-recovery.
- **Strict injection order:** system > role > skills (verbatim) > retrieved chunks > task file. Skills win on conflict.
- **`retrieve()` = one call per agent per task.** Scoped to the task's `skills_to_load`.
- **MCP rules:** `agentic_mcp.retrieve` is the internal knowledge plane; `dbhub` / `supabase` are external runtime tools usable only by implementer/tester. Planner/reviewer never touch external MCP.
- **Hooks = safety + logging.** No tool filtering, no context-aware gating.
- **State ownership:** every section of every task file has exactly one writing role. No agent overwrites another's section.

---

## Configuration (`.agentic.yml`)

```yaml
system_version: 1.0.0

enabled_skills:
  - fastapi
  - flutter
  - supabase

embedding:
  provider: voyage-3-lite       # falls back to fastembed if unavailable
  fallback: fastembed

gateway:
  deny:
    - "rm -rf"
    - "git push --force"
    - "DROP TABLE"
    # ...

retrieval:
  default_k: 6
  chunk_tokens: 400

plan_id:
  prefix: FEAT                  # FEAT-001, FEAT-002, ...
  pad: 3
  start: 1
```

---

## Adding a new skill

```
.claude/skills/<name>/
├── SKILL.md                    # Purpose / Rules / Patterns / Anti-patterns (≤ 3000 tokens)
└── references/
    ├── topic-a.md
    └── topic-b.md
```

Then add `<name>` to `enabled_skills` in `.agentic.yml`, run `/agentic-index`, and the planner can include it in any future task's `skills_to_load`. No agent or command edits needed.

---

## What's deliberately NOT built (v1)

- Tool catalog / routing / risk scoring
- Multi-retriever split (skills vs context vs patterns)
- Reranker
- `/agentic-recover` and automated retry/revert flows
- Parallel task dispatch
- Two-pass planner / architect review
- Security-analyzer role agent
- Advanced observability (`/agentic-stats`, run summaries)
- Stale-run detection, embedding cache, hash invalidation

Each of these is a **v2 layer**, built only when a real, repeated v1 failure mode points at it. The spec for the v2 trigger criteria lives in the original implementation plan.

---

## License

MIT — see [`LICENSE`](LICENSE).

# Agentic Development Workflow System

A Claude Code–native agentic development workflow. Installs per-project under `.claude/`. Agents are organized by **role** (planner / implementer / reviewer / tester), not by stack. Stack expertise lives in skill prompt blocks plus a retrievable references corpus.

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

## Automated UI testing

`/agentic-index` detects your project stack and writes `test_framework` to `.claude/state/project.yml`. The planner then automatically includes the right test skill on every tester task — no manual configuration.

| Stack | Detected by | Test framework | Viewable output |
|---|---|---|---|
| React / Next.js / Vue / web JS | `package.json` dependencies | Playwright MCP | Live headed browser + HTML report |
| Flutter / React Native / iOS / Android | `pubspec.yaml` or `android/` / `ios/` dirs | Maestro MCP | Maestro Viewer (device in browser) |

**How the tester works:**
1. Tester reads its injected skill (playwright or maestro)
2. Opens a real browser / device via MCP tools
3. Explores the live app — navigates, clicks, fills forms, screenshots each step
4. Generates `.spec.ts` (Playwright) or flow YAML (Maestro) from what it actually observed
5. Runs the artifact and reports results

The spec is always written from live exploration — never generated blind from the task description.

**For existing projects (brownfield):**
```
/agentic-test init    # scan existing screens/routes, ask which flows to cover, generate baseline tests
```

---

## Layout in an installed project

```
.claude/
├── .agentic.yml                # enabled_skills, embedding, deny patterns, plan_id, test_framework
├── settings.json               # hooks + MCP servers (all _agentic_managed: true)
├── agents/
│   ├── planner.md              # decomposition only — no file names, no classes
│   ├── implementer.md          # one task; loads skills verbatim; one retrieve() call
│   ├── reviewer.md             # append-only ## Review; never finalizes done
│   └── tester.md               # generic — testing method defined by injected skill
├── commands/
│   ├── agentic-plan.md
│   ├── agentic-build.md        # fixed topo-sort dispatcher contract
│   ├── agentic-status.md
│   ├── agentic-index.md        # embeds references + detects stack
│   ├── agentic-update-plan.md
│   ├── agentic-adr.md
│   ├── agentic-doctor.md
│   └── agentic-test.md         # brownfield test bootstrap
├── skills/
│   ├── fastapi/    SKILL.md + references/*.md
│   ├── flutter/    SKILL.md + references/*.md   # Riverpod 3.x
│   ├── supabase/   SKILL.md + references/*.md
│   ├── owasp/      SKILL.md + references/*.md   # web security, auto-included on HTTP surface
│   ├── html-css/   SKILL.md + references/*.md   # vanilla HTML/CSS/JS
│   ├── react/      SKILL.md + references/*.md   # React 18+, hooks, data fetching
│   ├── playwright/ SKILL.md + references/*.md   # browser UI testing via Playwright MCP
│   └── maestro/    SKILL.md + references/*.md   # mobile UI testing via Maestro MCP
├── hooks/
│   ├── pre_tool_use.py         # hard safety only (rm -rf, force push, DROP, TRUNCATE, ...)
│   ├── post_tool_use.py        # JSONL logger, one file per run_id
│   └── lib/
│       ├── retriever.py        # sqlite-vec + voyage/fastembed
│       ├── plan_id.py          # FEAT-### allocator
│       ├── adr_id.py           # ADR-### allocator
│       ├── git_branch.py       # feat/{run_id} branch helper
│       └── doctor.py           # install health checks
├── mcp/
│   └── agentic_mcp.py          # exposes retrieve(query, k, skills) over stdio MCP
└── state/                      # gitignored
    ├── plans/<FEAT-###>/
    │   ├── _overview.md
    │   └── t1.md, t2.md, ...
    ├── project.yml             # detected stack (test_framework)
    ├── vectors.db
    └── logs/<FEAT-###>.jsonl
```

`.mcp.json` at project root (seeded on install):
```json
{
  "mcpServers": {
    "agentic_mcp": { ... },      // internal retrieve() — always on
    "playwright":  { ... },      // browser automation — no credentials needed
    "maestro":     { ... },      // mobile automation — requires Maestro CLI
    "dbhub":       { ... },      // fill in DSN to enable
    "supabase":    { ... }       // fill in PAT + project-ref to enable
  }
}
```

---

## Core invariants (v1)

- **1 task = 1 file.** The task file is the single source of truth.
- **State machine:** `pending → running → done | blocked`. Terminal states are terminal; no auto-recovery.
- **Strict injection order:** system > role > ADRs > skills (verbatim) > retrieved chunks > task file. Skills win on conflict.
- **`retrieve()` = one call per agent per task.** Scoped to the task's `skills_to_load`.
- **Skills define HOW, agents define WHAT.** The tester role is generic — its testing method (MCP tools, commands, artifact format) is entirely defined by the injected skill. No framework logic in the role prompt.
- **MCP rules:** `agentic_mcp.retrieve` is the internal knowledge plane; `playwright` / `maestro` / `dbhub` / `supabase` are runtime tools usable only by implementer/tester. Planner/reviewer never touch external MCP.
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
  - owasp
  - html-css
  - react
  - playwright
  - maestro

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

# Set automatically by /agentic-index. Override manually if detection is wrong.
# Values: auto | playwright | maestro
test_framework: auto
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
- CI integration for generated test artifacts
- Visual regression testing
- Maestro Cloud integration

Each of these is a **v2 layer**, built only when a real, repeated v1 failure mode points at it.

---

## License

MIT — see [`LICENSE`](LICENSE).

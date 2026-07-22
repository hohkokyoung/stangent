# Agentic Development Workflow System

A Claude Code–native agentic development workflow. Installs per-project under `.claude/`. Agents are organized by **role** (planner / sketcher / architect / security-reviewer / implementer / reviewer / tester / debugger / refactor / auditor), not by stack. Stack expertise lives in skill prompt blocks plus a retrievable references corpus.

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

Cross-platform (Windows / macOS / Linux). Safe to re-run — system dirs (`agents/`, `commands/`, `hooks/`, `mcp/`) are always refreshed. Config files (`.agentic.yml`, `settings.json`, `.mcp.json`) are seeded on first install and left untouched on re-install so project-specific settings survive upgrades.

Runtime dependencies in the target project:
```bash
pip install pyyaml fastembed sqlite-vec
# optional: pip install voyageai && export VOYAGE_API_KEY=...   (better embeddings)
```

If you install these into a project virtualenv (`.venv/`, `venv/`, `env/`, or an active `$VIRTUAL_ENV`), the `agentic_mcp` retrieve server auto-detects that interpreter — it does not have to match the `python3` that launches the MCP server. Otherwise install the deps into the same `python3` that's on Claude Code's PATH.

## Uninstall

```bash
python <repo>/installer/agentic.py --target /path/to/project --uninstall
```
Removes only `_agentic_managed` hooks/MCP entries from `settings.json`, system-owned directories, and the `# >>> agentic` block from `.gitignore`. Anything you added is left alone.

---

## Per-feature workflow

In the installed project, in Claude Code:

```
/agentic-index                              # one-time setup (or when skills/references change)
/agentic-plan <natural-language goal>       # planner clarifies, sketches UI, emits FEAT-### task files
/agentic-build all                          # dispatcher runs tasks in dep order, re-indexes code before each
/agentic-review [commits:N | dir:path | all] # FULL review — hygiene + design + security in one pass → consolidated report → remediate
/agentic-review-design [run_id | "feature"] # architect red-teams the DESIGN — data ownership, tenancy, compliance, scaling
/agentic-review-security [run_id | "feature"] # security-reviewer red-teams for exploits — OWASP Top 10, IDOR, injection, secrets
/agentic-review-pr <PR# | url> [--comment]  # fetch a GitHub PR → architect + security-reviewer; optional summary comment
/agentic-open-pr [run_id]                   # open a PR from a completed run's feat/<run_id> branch
/agentic-refactor <refactoring goal>        # clarify scope, run refactor agent, verify tests stay green
/agentic-status [--all]                     # dashboard (one run, or every run incl. parked features)
/agentic-update-plan <run-id> <amendment>   # amend without touching done tasks
/agentic-defer [run-id] <reason>            # park a half-finished run — freeze tasks, write dossier to docs/features/
/agentic-resume [run-id]                    # unfreeze a deferred run once its external blocker clears
/agentic-debug <bug description>            # diagnose a live bug — data first, code second
/agentic-screenshot [all | <slugs>]         # screenshot every page/screen into docs/screenshots/<date>/
```

The planner is strict — it walks an 11-dimension coverage checklist (scope, functional, acceptance, edges, auth, validation, error UX, data model, API, NFRs, out-of-scope) and asks via `AskUserQuestion` on blocking gaps, up to 4 rounds. **It makes no assumptions** — every gap must be answered by the developer before planning proceeds.

---

## Sketch — UI mockups before any code is written

The **sketcher** is a unique role that fires automatically during `/agentic-plan` for any task that involves a visible UI change. Before the implementer ever touches a file, the sketcher:

1. Reads the task's `## Goal` and `## Requirements`
2. Generates a self-contained HTML mockup (plain HTML + inline CSS, no frameworks)
3. Renders it via the Preview MCP — viewport is `390×844` for mobile projects (`test_framework: maestro`) or `1280×800` for web/unknown
4. Screenshots it and embeds the image in the **implementer's** task file under `## Sketch`

The implementer then uses the sketch as a visual spec — not a description, an actual rendered image. This prevents the classic loop of "implement → review → redesign → re-implement."

The sketcher writes **no framework code**. It produces exactly one image and stops.

### Design source: Claude Design (optional)

By default the sketch is a throwaway HTML file — the PNG is the only artifact. Set the design source to `claude-design` in `.agentic.yml` to make the design a living, editable artifact on [claude.ai/design](https://claude.ai/design) instead:

```yaml
design:
  source: claude-design   # default: html
  fallback: html          # used when DesignSync is unavailable
  project_id: ""          # filled in automatically on first /agentic-plan
  remote_prefix: screens
```

How it works:

1. **Draft + push** — during `/agentic-plan`, the sketcher generates its mockup as usual, saves it under `.claude/design/screens/<run-id>/<task-id>.html`, and pushes it to your Claude Design project via the built-in `DesignSync` tool (first run asks you to pick or create a project).
2. **Edit visually** — open the design on claude.ai/design and polish it: comment inline, tweak spacing/colors/layout, restructure freely.
3. **Pull + refresh** — when `/agentic-build` runs, it compares each screen against the remote project. If you edited a design, it pulls your version down and re-runs the sketcher in refresh mode (re-render + re-screenshot only) before the implementer starts.

The implementer receives both the rendered PNG **and** the synced HTML — so exact spacing, colors, and typography from your edited design carry into the implementation, not just an eyeballed screenshot.

If DesignSync is unavailable (no claude.ai login, tool missing), everything falls back to the classic HTML flow — a plan or build never halts over it.

> **Upgrading an existing install:** `.agentic.yml` is seeded once and never overwritten on re-install, so add the `design:` block manually to projects installed before this feature.

---

## Automated UI testing

`/agentic-index` detects your project stack and writes `test_framework` to `.claude/state/project.yml`. The planner then automatically includes the right test skill on every tester task — no manual configuration.

| Stack | Detected by | `test_framework` value |
|---|---|---|
| React / Next.js / Vue (frontend) | `package.json` + no server-only markers | `playwright` |
| Flutter / React Native / iOS / Android | `pubspec.yaml` or `android/` / `ios/` dirs | `maestro` |

**How the tester works:**
1. Reads its injected skill — the skill defines the complete testing method (tools, commands, artifact format)
2. If a test runner MCP is available (e.g. Playwright, Maestro), uses it to explore the live app
3. Generates test artifacts (`.spec.ts`, flow YAML, `.py`, etc.) from actual exploration
4. Runs the artifact and reports results

The testing method is fully defined by the injected skill — the tester role itself contains no framework-specific logic.

**For existing projects (brownfield):**
```
/agentic-test init    # scan existing screens/routes, ask which flows to cover, generate baseline tests
```

---

## Project file indexing

`/agentic-index` indexes both skill references **and your own codebase** into `vectors.db`. When `/agentic-build` runs, it re-indexes project files (incremental, hash-cached) before dispatching each task — so every implementer agent's `retrieve()` calls can find code written by earlier tasks in the same run.

- Skills are fully re-embedded only when you run `/agentic-index` manually (they rarely change).
- Project files are incrementally re-indexed before every task (only changed files are re-embedded).
- Both are stored under `skill="project"` in `vectors.db` and retrieved the same way as skill chunks.

---

## Screenshot capture

`/agentic-screenshot [all | <slugs>]` walks the running app and saves screenshots to `docs/screenshots/<date-time>/` in your project root — ready for a README, portfolio, or design docs.

```
/agentic-screenshot               # interactive — asks which pages/screens and URL
/agentic-screenshot all           # auto-discover all static routes/screens and capture everything
/agentic-screenshot home login    # capture specific pages by slug
```

**Hard gates:**
- Requires `test_framework` set in `.claude/state/project.yml` (run `/agentic-index` first)
- Probes the MCP server before doing anything — if Playwright or Maestro does not respond, it stops immediately with a fix checklist
- If Maestro returns no connected device, it stops

**What it captures:**
- Web (Playwright): desktop 1280×800 + mobile 390×844 by default, navigates each route, waits for DOM before shooting
- Mobile (Maestro): taps through each screen from the app home, confirms via view hierarchy before shooting

**Output:**
```
docs/screenshots/<YYYY-MM-DD_HH-MM>/
├── 01-home-desktop.png
├── 01-home-mobile.png
├── 02-login-desktop.png
└── README.md           ← auto-generated index with embedded images
```

A single page/screen failure does not abort the run — it is logged and the capture continues.

---

## Debugging — data first

`/agentic-debug <description>` runs the **debugger** agent, which follows a strict order:

1. **Queries the database first** — uses any available DB MCP tool (`mcp__supabase`, `mcp__dbhub`) or falls back to CLI (`psql`, `sqlite3`, `mysql`, etc.) — fetches actual rows, checks for nulls, missing foreign keys, access-control violations
2. **Reads the code second** — only after knowing what the data actually contains
3. **Correlates** — matches data shape against what the code expects
4. Writes a structured diagnosis report to `.claude/state/debug/<DBG-id>.md`

The debugger writes nothing to the codebase. Its output is a diagnosis and a single suggested next step — from there you use `/agentic-plan` or `/agentic-update-plan` to act on it.

---

## Deferring half-finished features

Sometimes a run stops for reasons no agent can fix — the backend isn't deployed yet, credentials are pending, another team isn't ready. That's not `blocked` (an agent failing at its job) and not a scope change: *the plan is fine, the world isn't ready.* And run state under `.claude/state/` is gitignored working memory — none of it written for humans — so simply walking away means the run's context evaporates on a fresh clone or after a few months of forgetting.

`/agentic-defer [run-id] <reason>` parks the run durably:

1. Every non-`done` task is frozen: `status: deferred`, `blocker: "external: <dependency>"`, plus a `resume_when:` condition ("backend `/health` returns 200 on staging" — observable, not "later").
2. A **committed** handoff dossier is written to `docs/features/<run-id>-<slug>.md` (from `templates/feature-dossier.md`): goal, what shipped with its key decisions, what's half-done, why it stopped, branch + last commit, a resume checklist, and any context that lives nowhere else.
3. The run is registered in `docs/FEATURES.md` — a one-table registry of every parked and shipped feature.

`/agentic-build` never dispatches deferred tasks (dependents freeze with them), and `/agentic-status --all` shows parked runs across the whole project — including on a fresh clone, where the dossier is the only surviving record.

When the blocker clears, `/agentic-resume [run-id]` verifies the `resume_when` condition, flips the frozen tasks back to `pending`, checks how far the feature branch drifted from the base, and points you at `/agentic-update-plan` (if assumptions changed while parked) or `/agentic-build all`.

Both commands also work for features that were **never planned through `/agentic-plan`**: with no qualifying run, defer writes just the dossier and registry row (`Run` column `-`), and resume seeds a fresh `/agentic-plan` from the dossier's *What's half-done / remaining* section. Defer never freezes a run whose goal doesn't match the deferral reason — a run must be named or confirmed, never guessed from recency.

Ownership is strict: only `/agentic-defer` sets `deferred`, only `/agentic-resume` clears it. Agents and the planner never touch deferral state — an agent that can't proceed for an in-run reason uses its own blocker codes.

---

## Layout in an installed project

```
.claude/
├── .agentic.yml                # enabled_skills, embedding, deny patterns, plan_id
├── settings.json               # hooks + MCP servers (all _agentic_managed: true)
├── agents/
│   ├── planner.md              # decomposition only — no file names, no classes, no assumptions
│   ├── sketcher.md             # renders HTML mockup → screenshot → embeds in task file
│   ├── architect.md            # system-level design review; challenges assumptions incl. ADRs; report only
│   ├── security-reviewer.md    # red-team threat model — OWASP Top 10, IDOR, injection, secrets; report only
│   ├── implementer.md          # one task; loads skills verbatim; one retrieve() call
│   ├── reviewer.md             # append-only ## Review; never finalizes done
│   ├── tester.md               # generic — testing method defined by injected skill
│   ├── debugger.md             # data first, code second; writes diagnosis only
│   ├── refactor.md             # no new behavior; runs tests before/after; blocks on regression
│   └── auditor.md              # codebase-wide smell scan; writes findings report only
├── commands/
│   ├── agentic-plan.md
│   ├── agentic-build.md        # fixed topo-sort dispatcher; re-indexes project before each task
│   ├── agentic-status.md
│   ├── agentic-index.md        # embeds skill references + indexes project source files
│   ├── agentic-update-plan.md
│   ├── agentic-defer.md        # park a run on an external blocker → committed dossier in docs/features/
│   ├── agentic-resume.md       # unfreeze a deferred run once its blocker clears
│   ├── agentic-adr.md
│   ├── agentic-doctor.md
│   ├── agentic-debug.md        # data-aware bug diagnosis
│   ├── agentic-refactor.md     # refactor with test-green guarantee
│   ├── agentic-test.md         # brownfield test bootstrap
│   ├── agentic-screenshot.md   # screenshot all pages/screens → docs/screenshots/<date>/
│   ├── agentic-cleanup.md      # audit for smells → dispatch refactor tasks
│   ├── agentic-review.md          # FULL review — hygiene + design + security → consolidated → remediate
│   ├── agentic-review-design.md   # architect design review → findings report
│   ├── agentic-review-security.md # security red-team → threat model report
│   ├── agentic-review-pr.md       # review a GitHub PR (github MCP) → architect + security-reviewer
│   ├── agentic-open-pr.md         # open a PR from a completed run (github MCP)
│   └── agentic-lessons.md      # distill recurring review findings → lessons the planner learns from
├── skills/
│   ├── fastapi/      SKILL.md + references/*.md
│   ├── rest-openapi/ SKILL.md + references/*.md  # REST API design, status codes, pagination, OpenAPI
│   ├── flutter/      SKILL.md + references/*.md  # Riverpod 3.x
│   ├── mobile/       SKILL.md + references/*.md  # cross-screen patterns (nav guards, optimistic UI, etc.)
│   ├── supabase/     SKILL.md + references/*.md
│   ├── owasp/        SKILL.md + references/*.md  # web security; add to enabled_skills to activate
│   ├── html-css/     SKILL.md + references/*.md  # vanilla HTML/CSS/JS
│   ├── react/        SKILL.md + references/*.md  # React 18+, hooks, data fetching
│   ├── playwright/   SKILL.md + references/*.md  # browser UI testing via Playwright MCP
│   └── maestro/      SKILL.md + references/*.md  # mobile UI testing via Maestro MCP
├── hooks/
│   ├── pre_tool_use.py         # hard safety only (rm -rf, force push, DROP, TRUNCATE, ...)
│   ├── post_tool_use.py        # JSONL logger, one file per run_id
│   └── lib/
│       ├── retriever.py        # sqlite-vec + voyage/fastembed; supports --project-only flag
│       ├── plan_id.py          # FEAT-### allocator
│       ├── adr_id.py           # ADR-### allocator
│       ├── git_branch.py       # feat/{run_id} branch helper; auto-increments to -v2, -v3 on collision
│       ├── log_dispatch.py     # structured dispatch events → .claude/state/logs/dispatch.jsonl
│       └── doctor.py           # install health checks
├── mcp/
│   └── agentic_mcp.py          # exposes retrieve(query, k, skills) over stdio MCP
└── state/                      # gitignored — local working memory; durable artifacts are promoted to docs/ and adrs/
    ├── plans/<FEAT-###>/
    │   ├── _overview.md
    │   ├── t1.md, t2.md, ...
    │   └── sketches/<task_id>.png
    ├── debug/<DBG-id>.md
    ├── design-review/<DR-id>/findings.md      # architect reports
    ├── security-review/<SEC-id>/findings.md   # security-reviewer threat models
    ├── project.yml             # detected stack (test_framework, project_index_globs)
    ├── vectors.db              # skill chunks + project source chunks
    └── logs/<FEAT-###>.jsonl
```

`.mcp.json` at project root (seeded on install):
```json
{
  "mcpServers": {
    "agentic_mcp":          { ... },  // internal retrieve() — always on
    // docs & research
    "context7":             { ... },  // always-fresh library docs — no credentials needed
    "fetch":                { ... },  // general URL fetching — no credentials needed
    // reasoning
    "sequential-thinking":  { ... },  // structured reasoning tool — no credentials needed
    // testing & mobile
    "playwright":           { ... },  // browser automation — no credentials needed
    "maestro":              { ... },  // mobile automation — requires Maestro CLI
    // version control
    "github":               { ... },  // fill in GITHUB_PERSONAL_ACCESS_TOKEN to enable
    // databases
    "dbhub":                { ... },  // fill in DSN to enable
    "supabase":             { ... }   // fill in PAT + project-ref to enable
  }
}
```

---

## Core invariants (v1)

- **1 task = 1 file.** The task file is the single source of truth.
- **State machine:** `pending → running → done | blocked`, plus `deferred` for external blockers — set only by `/agentic-defer`, cleared only by `/agentic-resume`, never by an agent. Terminal states are terminal; no auto-recovery.
- **Strict injection order:** system > role > ADRs > skills (verbatim) > retrieved chunks > task file. Skills win on conflict.
- **`retrieve()` = one call per agent per task.** Scoped to the task's `skills_to_load`.
- **Skills define HOW, agents define WHAT.** The tester role is generic — its testing method (MCP tools, commands, artifact format) is entirely defined by the injected skill. No framework logic in the role prompt.
- **Sketch before code.** For any task with visible UI changes, the sketcher runs during planning and embeds a rendered image before any implementer task is dispatched.
- **Debugger = diagnosis only.** The debugger never writes to the codebase. Data before code, always.
- **MCP rules:** `agentic_mcp.retrieve` is the internal knowledge plane; `context7` / `fetch` / `sequential-thinking` are available to all agents; `playwright` / `maestro` / `dbhub` / `supabase` / `github` are runtime tools usable only by implementer/tester/debugger. Planner/reviewer/sketcher never touch external MCP (except sketcher uses Preview MCP for rendering).
- **Hooks = safety + logging.** No tool filtering, no context-aware gating.
- **State ownership:** every section of every task file has exactly one writing role. No agent overwrites another's section.

---

## Configuration (`.agentic.yml`)

```yaml
system_version: 1.0.0

enabled_skills: []   # empty by default — add skills that match your stack
# available: react, html-css, flutter, mobile, fastapi, rest-openapi, supabase, owasp, playwright, maestro

embedding:
  provider: voyage-3-lite       # falls back to fastembed if unavailable
  fallback: fastembed

# Optional: override auto-detected project globs for source file indexing
# project_index:
#   include:
#     - "**/*.dart"
#     - "**/*.py"
#   exclude:
#     - "node_modules/**"

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

# test_framework is NOT set here. /agentic-index writes it to .claude/state/project.yml automatically.
# To override: edit .claude/state/project.yml directly after running /agentic-index.
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
- CI integration for generated test artifacts
- Visual regression testing (foundation exists via `/agentic-screenshot` — comparing across builds is v2)
- Maestro Cloud integration

Each of these is a **v2 layer**, built only when a real, repeated v1 failure mode points at it.

---

## License

MIT — see [`LICENSE`](LICENSE).

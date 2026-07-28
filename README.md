# Agentic Development Workflow System

A Claude Code–native agentic development workflow. Installs per-project under `.claude/`. Agents are organized by **role** (planner / sketcher / designer / architect / security-reviewer / implementer / reviewer / design-critic / tester / debugger / refactor / auditor), not by stack. Stack expertise lives in skill prompt blocks plus a retrievable references corpus.

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
/agentic-design ["<direction>"]             # author the UI design spec → docs/design/ (greenfield interview or brownfield extract+critique)
/agentic-review [commits:N | dir:path | all] # FULL review — hygiene + design + security (+ UI adherence) → consolidated report → remediate
/agentic-review-design [run_id | "feature"] # architect red-teams the DESIGN — data ownership, tenancy, compliance, scaling
/agentic-review-ui [run_id: | dir: | all]   # design-critic checks the built UI against docs/design/DESIGN-SPEC.md → drift report
/agentic-sweep <ui|security|audit> [dir:]   # EXHAUSTIVE review — every file × every rule, in computed batches
/agentic-review-security [run_id | "feature"] # security-reviewer red-teams for exploits — OWASP Top 10, IDOR, injection, secrets
/agentic-review-pr <PR# | url> [--comment]  # fetch a GitHub PR → architect + security-reviewer; optional summary comment
/agentic-remediate <review_id>              # turn an EXISTING review's findings into fix tasks → dispatch
/agentic-open-pr [run_id]                   # open a PR from a completed run's feat/<run_id> branch
/agentic-refactor <refactoring goal>        # clarify scope, run refactor agent, verify tests stay green
/agentic-status [--all]                     # dashboard (one run, or every run incl. parked features)
/agentic-update-plan <run-id> <amendment>   # amend without touching done tasks
/agentic-defer [run-id] <reason>            # park a half-finished run — freeze tasks, write dossier to docs/features/
/agentic-resume [run-id]                    # unfreeze a deferred run once its external blocker clears
/agentic-debug <bug description>            # diagnose a live bug — data first, code second
/agentic-screenshot [all | <slugs>]         # screenshot every page/screen into docs/screenshots/<date>/
/agentic-cleanup [commits:N | dir: | all]   # CODE cleanup — audit for smells, then dispatch refactor tasks to fix them
/agentic-clean-state [days:N] [--apply]     # STATE cleanup — prune old runs/logs + empty review dirs from .claude/state/
/agentic-adr new <title> | list | bootstrap # create/manage Architectural Decision Records; `bootstrap` back-fills from history
/agentic-lessons                            # distill recurring review findings into lessons the planner reads on every plan
/agentic-logs [id] [--json]                 # readable summary of a run/review's logs — tool counts, denials, failures, duration
/agentic-doctor [--json]                    # install health — deps, config, MCP, vectors.db, hooks, model ids, drift
```

`/agentic-cleanup` and `/agentic-clean-state` are easy to confuse: the first
rewrites **code**, the second prunes **`.claude/state/`**. Only the first
dispatches agents.

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

## UI design spec — author, recommend, enforce

The sketcher draws one screen at a time. The **design spec** is the tier above it:
a durable, committed house style the whole UI must obey. It's authored once,
enforced continuously, and honoured automatically when new screens are sketched —
closing the loop **spec → sketch → build → critique**.

```
/agentic-design                 # author or amend docs/design/DESIGN-SPEC.md
/agentic-review-ui all          # critique the built UI against it
```

`/agentic-design` auto-detects which of two modes it's in (and confirms with you):

- **Greenfield** (no UI yet) — it **interviews** you on aesthetic direction (vibe,
  colour, typography, density, **motion appetite**, platforms, a11y bar) and then
  **recommends a concrete stack** — motion library, component approach, theming,
  optional 3D — each with a one-line reason and **where to install it**. It never
  installs anything; the recommendations land in `docs/design/plugins.md` for you
  to act on.
- **Brownfield** (UI already exists) — the **designer** agent **extracts** the
  current design language from your code (tailwind config, `:root` tokens, theme
  files, component styles) into the spec, and **flags every inconsistency it hits**
  along the way (a colour expressed as two hex values, off-scale spacing, a button
  styled three ways, missing focus states). That drift report is the critique you
  asked for before you commit the spec.

The approved spec is promoted to committed `docs/design/`:

```
docs/design/
├── DESIGN-SPEC.md    ← the house style: colour roles, type scale, motion budget, a11y bar, do/don't
├── tokens.md         ← machine-diffable token table (the critic diffs against this)
└── plugins.md        ← recommended stack + where to install each piece
```

**Enforcement.** `/agentic-review-ui` runs the **design-critic**, which checks the
built UI against the spec — raw hex that should be tokens, spacing off the scale,
interactive elements with no focus/disabled state, contrast under the AA floor, and
internal inconsistencies where the UI contradicts itself. It's read-only: it writes
a verdict (`on-spec` | `drift` | `off-spec`) and a findings report, never touches
code. Its findings also fold into the umbrella `/agentic-review` as a fourth lane
(only when a spec exists) and flow into remediation there.

The critic works **statically** always (tokens/CSS/components), and uses the latest
`docs/screenshots/` (from `/agentic-screenshot`) as visual evidence when present.

Once a spec exists, every `/agentic-plan` sketch honours it — the sketcher reads
`DESIGN-SPEC.md` + `tokens.md` and draws in the house style, so mockups match the
product instead of a generic look.

> **No auto-install.** The designer only recommends and documents — you install the
> stack yourself. **Upgrading an existing install:** the new `designer` /
> `design-critic` model entries in `.agentic.yml` are optional (both fall back to
> `models.default`), so the feature works without editing config.

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

## Logging & observability

Every command that dispatches an agent establishes a **log context** — it writes
its workflow id to `.claude/state/current_run.txt` around the dispatch (build →
`FEAT-NNN`, reviews → `SEC-`/`DR-`/`UIR-`/`PR-`, debug → `DBG-`, design → `DS-`,
baseline tests → `baseline-<flow>`). Two hooks then capture activity under that
context:

- **`post_tool_use.py`** (PostToolUse) — one JSONL line per tool call →
  `logs/<id>.jsonl`: `{ts, run_id, task_id, agent_role, model, tool, ok, args, res_chars, deny_reason?, error?}`. Secrets in args are redacted.
  `res_chars` sizes the tool's *result* — what lands in the agent's context and
  is re-read every later turn. Since agentic cost is dominated by cache-read,
  this is what makes a run's bill traceable to the calls that caused it;
  `/agentic-logs` ranks the heaviest ones.
- **`budget` events** — the same hook warns, mid-run, when one task crosses a
  threshold on either of two axes: **call count** (150/300/500) for a task that
  will not terminate, and **cumulative result bytes** (800k/1.5M/3M chars) for a
  task whose calls are individually expensive. The second is the one that tracks
  the bill, and call count cannot stand in for it: on FEAT-025 one task did 34
  edits for $3.68 while another did 89 for $12.28 — the difference was that the
  second echoed a 25 KB file back into context on every edit. Replayed against
  that run, the bytes axis fires at call 43 where the call axis waits until 150,
  and stays silent on all six healthy tasks. Neither blocks; a long task can be
  legitimate. Instead the hook returns the warning as `additionalContext`, so it
  is injected as a system reminder beside the tool result and **the running agent
  reads it on its next turn**, naming the action ("script it") rather than only
  the number. Writing it to the run log alone would have made it forensics —
  nothing reads that log until someone runs `/agentic-logs` after the run, which
  is exactly when it can no longer help. On FEAT-025 the bytes threshold is
  crossed at call 43 of 247, leaving 200 calls to change course. The hook emits
  nothing on stdout unless a threshold was actually crossed; it fires on every
  tool call, so an unconditional emit would staple a reminder to every result.
- **`log_usage.py`** (SubagentStop) — one `usage` event per finished agent, with
  token counts (input/output/cache) and estimated cost, attributed to the task.
- **`log_dispatch.py`** — one routing event per build dispatch → `dispatch.jsonl`.
- **`hook_error` events** — every telemetry hook swallows its exceptions, because
  breaking a run to report a logging failure would be worse than the failure. But
  a silently dead hook is indistinguishable from an idle one: a run whose usage
  events stopped looks exactly like a run that produced none, so the cost table
  under-reports and nothing says why. Each hook now records `{event: hook_error,
  hook, error}` before swallowing, and `/agentic-logs` surfaces it above the
  per-task table. Best-effort by necessity — if the log is what failed, there is
  nowhere to write.

**Only agentic work is logged.** With no command active, tool calls are ambient
general dev and are skipped (they used to fill an unbounded `_no-run.jsonl`). Set
`AGENTIC_LOG_AMBIENT=1` to opt back in. Any single log rotates at 5 MB to
`<id>.1.jsonl`.

**Read the logs** with **`/agentic-logs [id]`** (or `python .claude/hooks/lib/logs.py summarize <id>`):

```
Run FEAT-024  2026-06-26 06:17 → 09:49  (3h32m)
  tasks: 8   tool calls: 233   retrieve: 8   get_symbol: 0   denials: 0   failures: 0
  cost: $2.14   tokens: in 40k out 88k cache-read 5.2M cache-write 310k   cache-hit 92%

  task   role         model        status  calls  ret  sym  deny  fail   dur    tok   cost
  t1     implementer  sonnet-5     done       56    1    3     0     0   33m    45k  $0.61
  ...
```

**Cost rates** are estimates in `token_cost.py`; override per project in
`.agentic.yml` under `pricing:`. **Retention:** `/agentic-clean-state` prunes old
per-run logs (and empty review dirs) by age.

---

## From findings to fixes

The four targeted reviews (`-ui`, `-security`, `-design`, `-pr`) are deliberately
advisory: they end at a report and change nothing. `/agentic-review` remediates,
but only from lanes it runs itself — it takes a *code scope*, never a review id.

`/agentic-remediate <review_id>` is the connector. It reads a finished
`findings.md`, re-runs `verify_clears.py` against it first, turns findings into
grouped tasks with roles and acceptance criteria, and dispatches through the same
path `/agentic-build` uses.

```
/agentic-review-ui all          # → UIR-… findings.md   (advisory)
/agentic-remediate UIR-…        # → FEAT-… tasks → dispatch
/agentic-review-ui all          # confirm the class is closed
```

Two details that matter:

- **Evidence is re-verified before anything is dispatched.** A report is a
  snapshot; code moves. A finding whose site list no longer reproduces is
  re-derived or dropped, never used as-is — otherwise you write tasks against
  sites that have already been fixed, or that never existed.
- **Re-review after remediating.** This fixes what a report *said*. Only a fresh
  review establishes the class is actually closed: one remediation pass fixed all
  eleven sites it was given and left a twelfth the original review never found.

---

## Review verification — clears, citations, and coverage

A reviewing agent's most damaging output is not a missed finding. It is a
checklist item reported as **cleared**. A miss leaves the reader still looking; a
clear tells them the item was checked and stops them. Every review command
therefore re-checks its own agent's work before showing it to you.

**1. Citations.** Anything an agent clears must carry evidence in one of two
forms, and findings cite the search behind their site list the same way:

```
- **<item>** — cleared by: `<single read-only command>` -> <N> matches
- **<item>** — cleared by: <path>:<line> "<exact snippet>"
**Where:** enumerated by: `<command>` -> <N> sites
```

**2. Re-running.** `hooks/lib/verify_clears.py` runs each cited command again and
re-reads each cited `file:line`, then reports what no longer reproduces — a count
that has moved, a command that errors, a snippet that is not there. It knows
nothing about the project, the language, or what any rule means; it only checks
that the claim still holds. That is what makes it work unchanged on a Flutter app,
a Django service, or a Rust CLI.

Commands are allowlisted read-only tools (`grep`, `rg`, `find`, `wc`, …) and may
pipe between them, but must not chain with `;`/`&&`, redirect, or substitute — a
chained command whose first stage fails silently produces a confident wrong count.

**3. Coverage.** Citations prove what was claimed; they cannot catch a rule the
review never examined, because that leaves no false claim behind — just silence,
which reads exactly like a rule that passed. So each review is checked against a
declared checklist and must carry one `## Coverage` row per item:

| review | checklist source | items |
|---|---|---|
| `/agentic-review-ui` | the project's `docs/design/DESIGN-SPEC.md` §13 | project-defined |
| `/agentic-review-security` | `.claude/agents/security-reviewer.md` | 8 attacker categories |
| `/agentic-review-design` | `.claude/agents/architect.md` | 7 design dimensions |

Missing rows fail the run. `unverified — <why>` **counts as coverage** — if
honesty did not satisfy the count, an agent would invent rows to make the number
work.

Checklists are plain markdown `- [ ]` task lists, which is the only syntax the
extractor knows. Reformat one and extraction silently returns zero items,
disabling enforcement for that source — `/agentic-doctor` checks for exactly that,
since it is a failure that otherwise looks like health.

**Findings carry a stable identity.** Verification makes one report trustworthy;
it says nothing about whether a class of finding ever *closed*. Answering that
means grouping two reports, so `/agentic-review-ui` findings tag their spec
section by number alone — `[§9]`, or `[§9, §3]` for two — and the command greps
for any tag that deviates. Titles drift immediately: across ten reviews of one
project the same section was written `[§2]`, `[§2 Principles]`, and
`[§2 Design principles]`, and `[§5]`, `[§5 Spacing]`, `[§5 Radius]` — which made
"is §9 closed yet?" a manual read of both reports, and that question is the whole
reason for reviewing twice. Section numbers come from `DESIGN-SPEC.md`'s own
headings (1-13) and are stable; the words after them are not.

**4. The search is declared, not invented.** Citations prove claims about the code
the review searched. They say nothing about whether that was the *right* code —
and a reviewer that writes its own search each run measures a different
population every time. Three reviews of one project checked the same rule with
three searches of their own devising:

```
BorderRadius.circular(        -> 189 sites   every radius site
BorderRadius.circular([0-9]   ->  24 sites   raw literals only
AppRadius\.                   -> 203 sites   token uses
```

None of them is wrong. They answer different questions, and nobody had decided
which question the item asks — so the numbers were never comparable and "is this
closed?" had no defined answer. **You cannot close a set that is redefined each
time you look at it.**

So the search belongs to the checklist item, not to the run. `docs/review/enumerations.md`
declares one read-only command per item, keyed by reviewer; `verify_clears.py`
compares every Coverage row against it and fails the run on a substitution, so
`N` in `inspected: k of N` means the same thing twice.

**Each declaration also carries a `kind`, and that is what decides whether a rule
can ever finish.** A `violations` search matches only what is wrong
(`circular([0-9]`): the population *is* the remaining work, a fixed site leaves
the set permanently, and the count drains 24 → 14 → 0 with zero meaning closed. A
`candidates` search matches everything that must be judged (`circular(` → all 189,
or `rawQuery` → every database call, because grep cannot tell a parameterised
query from a concatenated one): compliant sites stay forever, the count never
drains, and the item can never be reported closed by count.

That distinction is why reviews stop converging. Against a `candidates` search
there is always unexamined material left however diligent each run is, so every
pass turns up findings in code nobody touched — not regressions, just the part of
the set that run happened to reach. **Prefer `violations`**; where no violating
pattern exists (*"every interactive element renders a focus ring"*), leave the
item undeclared and let the review report `unverified` rather than invent a
denominator that cannot shrink.

A `baseline` column makes movement legible, and it too reads per kind: for
`violations`, above baseline is new bad code landing and fails the run; for
`candidates`, a collapse toward zero is a **broken search** — a moved path, a
renamed idiom — which is the most dangerous false success available here, since
it looks exactly like a completed rule.

This applies to the three reviewers whose checklists **enumerate sites**
(`design-critic`, `security-reviewer`, `auditor`). It deliberately does not apply
to `architect`, whose seven dimensions are questions about a design — *who owns
this entity, what breaks at 100×, can one tenant reach another's data* — with no
population to search. The file is optional; without it reviews improvise exactly
as before, and `/agentic-doctor` reports that they are not comparable.

**What this does not do.** It verifies that stated claims are true. It cannot tell
you the right questions were asked: a review against a vague spec will cite
honestly, cover every row, and still find little. **Verified is not the same as
complete.**

---

## Layout in an installed project

```
.claude/
├── .agentic.yml                # enabled_skills, embedding, deny patterns, plan_id
├── settings.json               # hooks + MCP servers (all _agentic_managed: true)
├── agents/
│   ├── planner.md              # decomposition only — no file names, no classes, no assumptions
│   ├── sketcher.md             # renders HTML mockup → screenshot → embeds in task file
│   ├── designer.md             # authors/extracts the UI design spec + recommends a stack; draft only
│   ├── design-critic.md        # checks the built UI against the design spec; drift report only
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
│   ├── agentic-design.md       # author/amend the UI design spec → docs/design/ (greenfield or brownfield)
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
│   ├── agentic-cleanup.md      # CODE cleanup: audit for smells → dispatch refactor tasks
│   ├── agentic-clean-state.md  # STATE cleanup: prune old runs/logs + empty review dirs
│   ├── agentic-logs.md         # readable per-run log report (tool counts, denials, duration)
│   ├── agentic-review.md          # FULL review — hygiene + design + security (+ UI) → consolidated → remediate
│   ├── agentic-review-design.md   # architect design review → findings report
│   ├── agentic-review-security.md # security red-team → threat model report
│   ├── agentic-review-ui.md       # design-critic checks built UI vs design spec → drift report
│   ├── agentic-sweep.md           # EXHAUSTIVE review (ui|security|audit) — computed batches, coverage is arithmetic
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
│       ├── git_branch.py       # feat/{run_id} branch helper (-v2/-v3 on collision) + per-task checkpoint commits
│       ├── sweep_plan.py       # deterministic batch planner for /agentic-sweep
│       ├── log_dispatch.py     # structured dispatch events → .claude/state/logs/dispatch.jsonl
│       ├── logs.py             # summarize a run/review's logs (/agentic-logs)
│       └── doctor.py           # install health checks
├── mcp/
│   └── agentic_mcp.py          # exposes retrieve() + get_symbol() over stdio MCP
├── .install.json               # gitignored — per-install record read by /agentic-doctor (see "Knowing whether an install is current")
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
    "agentic_mcp":          { ... },  // internal retrieve() + get_symbol() — always on
    // docs & research
    "context7":             { ... },  // always-fresh library docs — no credentials needed
    // (URL fetching is Claude Code's built-in WebFetch/WebSearch — no server needed)
    // reasoning
    "sequential-thinking":  { ... },  // structured reasoning tool — no credentials needed
    // testing & mobile
    "playwright":           { ... },  // browser automation — no credentials needed
    "maestro":              { ... },  // mobile automation — requires Maestro CLI
    // version control
    "github":               { ... },  // remote HTTP server — OAuth on first use, no token stored
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
- **Every finished task is checkpointed.** `/agentic-build` commits each task's work onto the run's branch as soon as the subagent returns, so a task that damages earlier work has a boundary to fall back to (`git revert`, or reset to the last task's sha). Local only, never pushed, meant to be squashed before a PR. It is taken by the dispatcher rather than the agent because `pre_tool_use.py` denies `git commit` to any subagent — and it is skipped, never forced, when the branch was switched mid-run, a pre-commit hook rejects, or the project isn't a git repo. Disable with `git.checkpoint_commits: false`.
- **Mechanical changes are scripted, not hand-edited.** When one transformation applies to more than ~5 sites (literals onto tokens, a rename across a package, one lint fix repeated), the implementer writes and runs a codemod, then verifies with the project's own checks — it does not edit each site in turn. N sequential edits to one file cost on the order of N², because every `Edit` returns its file into context and every later turn re-reads it. A script is also *more* reliable: it cannot silently miss site 17 of 21. Per-site changes, where each occurrence needs its own judgment, are still edited individually.
- **Sketch before code.** For any task with visible UI changes, the sketcher runs during planning and embeds a rendered image before any implementer task is dispatched.
- **Design spec is the house style.** Authored (greenfield) or extracted (brownfield) by `/agentic-design` into committed `docs/design/`. Agents draft to gitignored state; the command promotes on approval. The sketcher honours it; the design-critic enforces it (`/agentic-review-ui`). Optional — projects with no frontend simply never author one.
- **Debugger = diagnosis only.** The debugger never writes to the codebase. Data before code, always.
- **MCP rules:** `agentic_mcp.retrieve` is the internal knowledge plane; `context7` / `sequential-thinking` are available to all agents; `playwright` / `maestro` / `dbhub` / `supabase` / `github` are runtime tools usable only by implementer/tester/debugger. Planner/reviewer/sketcher never touch external MCP (except sketcher uses Preview MCP for rendering).
- **Hooks = safety + logging.** No tool filtering, no context-aware gating.
- **State ownership:** every section of every task file has exactly one writing role. No agent overwrites another's section.

---

## Configuration (`.agentic.yml`)

```yaml
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

models:                          # per-role model; "" = inherit the session model
  default:     claude-sonnet-5
  reviewer:    claude-sonnet-5
  tester:      claude-haiku-4-5-20251001
  security-reviewer: claude-opus-5
  # ...
```

**An unrecognised model ID does not error — it silently falls back to whatever
your session is running.** So routing reliably moves *down* (a valid cheaper ID
is honoured) and never *up*: naming a model more capable than your session's is a
no-op if the ID is wrong, and the config then states an intent it cannot deliver.
Cost telemetry stays correct throughout, because it prices the model the
transcript reports — which is why this hides. On one project every role
configured `claude-sonnet-4-6` ran as `claude-sonnet-5`, and `claude-opus-4-8`
for `architect`/`security-reviewer` never once dispatched to Opus. Only the haiku
entry, a real ID, was honoured.

`/agentic-doctor` now checks every `models:` value against
`model_capability_order` and warns on anything unranked. After changing these,
confirm a `usage` event in `.claude/state/logs/` reports the model you asked for.

**Don't cheap out on the reviewing roles.** Reviewing looks mechanical from the
outside — check the work against a written rule — so it is the first thing anyone
cost-optimizes. Enumeration *is* mechanical, and a small model does it well. But
the findings that matter usually need two or three facts joined across files, and
a small model's failure mode there is not silence: it reports the checklist item
as **cleared**. That reads as "checked and fine" and stops anyone looking again,
which is worse than no review. Report length is no signal either — in the run
that prompted this, the cheap reviews were the *longest*.

So `reviewer`, `design-critic`, `architect`, and `security-reviewer` sit at Sonnet
or above, and `complexity_routing.never_downgrade` keeps `low_cap` from pulling
them back down on low-complexity tasks (they can still be routed *up* by
`high_floor`). Independently, every one of these agents must now cite evidence for
each item it clears — a `file:line`, a computed value, a command and its result —
and report anything it could not check as `unverified` rather than clearing it.
That helps at any model tier, but it is not a substitute for one that can do the
joining.

**Per-role models & the two harnesses.** `.agentic.yml` `models:` is the single
source of truth. On install, the installer **stamps each agent's frontmatter
`model:`** from it (templates ship model-agnostic). This matters because the two
Claude harnesses differ: **Claude Desktop honors frontmatter `model:` but ignores
invocation-time model overrides**, while the **CLI** honors the invocation model
that `/agentic-build` passes for per-task complexity routing. Stamping the
frontmatter makes Desktop run each agent on its role model; in the CLI the
invocation override still wins, so dynamic routing is preserved. Edit `models:`
then re-run the installer (or `--upgrade-config`) to re-stamp; `--upgrade-config`
also back-fills role entries added in newer versions.

---


## Reviewing everything, not sampling

`/agentic-review-ui` reads what it judges worth reading. That is why it is cheap,
and it is also why it cannot tell you what it did not look at — the codebase does
not fit in one context, so the agent samples, and sampling is unrepeatable. Two
runs over identical code examine different subsets and each reports findings the
other missed. That is the mechanism behind "I fixed everything it found, ran it
again, and got new problems in files I never touched."

`/agentic-sweep` removes the choosing, the same way `dispatch_plan.py` removed
it from task ordering. `sweep_plan.py` computes the file list and splits it into
batches; the command dispatches every one; the agent only judges what it is
handed. Each batch checks **every** rule against **every** file it was given — the
inversion that matters, because a review that walks rules hunting for sites cannot
see the sites it never thought to hunt in.

```
sweep: 164 files, 1390KB, 20 batches
  b01  11 files   71KB  mobile/lib
  b02  16 files   78KB  mobile/lib/core/providers
  ...
sweep-verify: 19/20 batches reported, 162/164 files covered
  [FAIL] batches never reported: [20]
```

Coverage stops being a claim and becomes arithmetic: every in-scope file is in
exactly one batch, and `verify` fails when a batch never reported. A partial
sweep reads exactly like a complete one, so that check is the whole guarantee.

**Batch size trades cost against attention, in both directions.** Each batch
re-pays the fixed overhead (role prompt, spec, evidence policy), so halving
`--max-chars` nearly doubles the spend without reading any file more carefully.
Going too large means checking every rule against 50k tokens at once, which is
where an agent skims — reintroducing sampling inside a batch. The 80k default
lands around 8-16 files per batch on real code.

**What it guarantees is that nothing was skipped — not that nothing was missed.**
A critic reading a batch can still overlook a violation inside it, exactly as a
human reviewer can. Coverage is not detection. And rules that are not in the
source at any batch size — contrast, focus rings, rendered states — stay
`unverified` and need `/agentic-screenshot` plus a real device pass.

Three lanes share the one algorithm — `ui` (design-critic against the design
spec), `security` (the eight attacker categories), `audit` (the smell classes).
`ui` sweeps the UI surface; the other two sweep all source, since an IDOR is not
confined to widget files, and cost scales accordingly.

There is deliberately no `design` lane: the architect's dimensions are questions
about a design, not rules applied to files. Nor a PR lane — a diff is already a
bounded, stable population.

Use the sampling review for a quick read of recent work; use the sweep when a
rule needs actually closing.

---

## Knowing whether an install is current

System directories are mirrored on re-install — replaced wholesale, so a stale
file from an older version disappears. That has two consequences nothing used to
surface: a hand-edited agent is **destroyed without warning** on the next
install, and there was no way to ask whether an install was behind the templates
it came from — the old `system_version` could not answer it (nothing bumped it),
so the only method was byte-comparing against the source tree by hand.

Every install now writes `.claude/.install.json` — version, timestamp, the source
path it was installed from, and **two hashes per system file**: `tpl` as the file
ships, `cur` as it landed on disk. Two, because they legitimately differ: the
installer stamps role models into agent frontmatter, so a single hash would flag
all twelve agents as locally edited the moment they were installed.

`/agentic-doctor` reads it and reports three things:

```
[ok]    install manifest              @38ccb3e, installed 2026-07-27T11:33:40Z, 166 files tracked
[warn]  local edits to system files   1 edited since install: agents/reviewer.md — these are
                                      OVERWRITTEN by the next install; move the change into
                                      the template repo to keep it
[warn]  up to date with source        3 changed (agents/planner.md, …); 1 new (…) — re-run
                                      the installer to update
```

Only mirrored directories are tracked. Seed files (`.agentic.yml`,
`settings.json`) are user config — editing them is the documented workflow and is
never reported as drift. If the source tree has moved or is gone, the check says
it **cannot tell** rather than implying the install is current. The manifest is
gitignored: it holds an absolute local path and is regenerated on every install.

---

## Development

```bash
python3 -m unittest discover installer/tests
```

No third-party dependency beyond `pyyaml`. CI runs the suite on Python
3.10, 3.12, and 3.14 for every push and pull request
([`.github/workflows/tests.yml`](.github/workflows/tests.yml)); 3.10 is the
verified floor.

The suite covers the deterministic half of the system — dispatch planning,
citation parsing and sandboxed re-execution, retrieval, symbol extraction, state
hygiene, logging, cost, the hooks, and the installer. Agent *behaviour* is not
unit-testable and is covered separately by `.claude/evals/` (see
[`evals/README.md`](installer/templates/.claude/evals/README.md)).

**Running without PyYAML is verified, not assumed.** The runtime carries
`yaml = None` fallbacks throughout, so a project that never installed the
dependency still works — on defaults. A second CI job runs the whole suite with
the parser absent and it must pass, skipping only the three cases that genuinely
cannot run without one. Everything else was rewritten to not need a parser it was
only using for convenience: ten routing tests built their config by parsing a YAML
fixture, though the function under test takes a plain dict.

What that surfaced is worth knowing when you run this way: **config is silently
ignored without PyYAML.** A deliberate `checkpoint_commits: false`, a per-role
model, a `complexity_routing` rule — none of them apply, and the setting looks
honoured. `git_branch.py` and `dispatch_plan.py` now warn on stderr when
`.agentic.yml` exists but cannot be read (matching `retriever.py`, which already
did), and stay quiet when there is no config to ignore.

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

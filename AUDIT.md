# Stangent Audit

Internal review of consistency, robustness, and gaps versus an ideal agentic dev workflow. Produced 2026-05-24.

This document is for the maintainer. Not part of the user-facing system. Refer to it when picking work to prioritise.

Items are grouped by severity:
- **🔴 Bug** — broken or inconsistent, will misbehave in practice
- **🟡 Drift** — stale or out-of-sync, will mislead readers but won't crash
- **🟢 Gap** — missing capability, opportunity to improve
- **🔵 Concept** — architectural observation, worth thinking about

---

## 🔴 Bugs (functional inconsistencies that will cause incorrect behaviour)

### B1. `section-ownership.md` is incomplete and outdated
**File:** `prompts/section-ownership.md`

Missing sections that planner and other agents actually write:
- `## Codebase Context` (planner, written in Phase 1c2)
- `## Risks & Mitigations` (planner, written in Phase 4a)
- `## Performance Review` (reviewer, written in Phase 3d — NEW)
- `## Quality Review` (reviewer, written in Phase 3d — NEW)
- `## Context Checkpoint` (any agent, when context budget exhausted)

Result: agents reading this prompt won't know who owns these sections. The gateway doesn't enforce section ownership anyway (it only enforces paths), so this is documentation drift more than a runtime bug — but it's the canonical reference, so it must be correct.

### B2. `memory.md` template doesn't match the write protocol
**Files:** `templates/memory.md` vs `prompts/memory.md`

Template Feature History header:
```
| Feature | Title | Retries | Key Files Touched | Outcome |
```

Prompt Feature History entry format:
```
| {feature_id} | {title} | {retry_count} | {replan_count} | {key_files} | {outcome} |
```

The template is missing the `Replan Count` column. Agents following the prompt will write 6-column rows into a 5-column table → corrupted markdown table on every COMPLETE.

### B3. Reviewer Confidence references a phase that no longer exists
**File:** `agents/reviewer.md`

Phase 6f's confidence flag reads:
> `cross_stack_drift_found`: Phase 6 found schema/model mismatches → -10

But after the parallel-reviewer refactor:
- Phase 6 is now "Issue Verdict"
- Cross-stack drift is Phase 4

The deduction logic still works (it's checking the flag, not the phase name) but the documentation is wrong.

### B4. Reviewer OUTPUT CONTRACT doesn't list new sections
**File:** `agents/reviewer.md` lines 316–322

Still says only:
> Writes: ## Scope Verdict, ## Review Checklist, ## Review Verdict

After parallel-reviewer changes, also writes:
- `## Performance Review` (under Review Checklist)
- `## Quality Review` (under Review Checklist)

The agent will still write them (Phase 3d says so), but the OUTPUT CONTRACT section is misleading.

### B5. Reviewer frontmatter description is stale
**File:** `agents/reviewer.md` lines 4–8

> Runs the structured language-specific review checklist, enforces spec compliance, spawns the security scanner, and issues a severity-graded verdict.

Now spawns 3 specialists in parallel, not just security scanner.

### B6. `/uninit` Step 7 has a hardcoded command list that's out of date
**File:** `commands/uninit.md` lines 145–148

Listed commands:
```
abandon, adr, cleanup, doctor, feature, gateway, implement, plan, resume, review, srs, status, uninit
```

Missing: `debug.md`, `refine.md`, `pr.md`, `stats.md`.

Hard-coded enumeration means every new command needs a corresponding `/uninit` edit. Should auto-discover from `commands/` dir (or read from `init_constants.py`).

Same problem in `/uninit` Step 2 inventory.

### B7. `/doctor` Step 5 mismatch with reality (post-recent commits)
**File:** `commands/doctor.md`

Already updated in commit `ee0b209` to add debug + parallel reviewer agents and observer. **Verify the edit landed correctly** — the audit didn't catch this until after fixes were applied.

### B8. `validate_handoff.py` doesn't know about `tier`
**File:** `scripts/validate_handoff.py`

The post_planning validator requires ALL of: Scope, Out of Bounds, Files to Touch, Acceptance Criteria, contract file. Direct tier writes all of these so it passes today.

But Direct tier omits sections the validator doesn't check (Risks, ADRs Applied, Codebase Context, Planner Confidence). If a future check is added requiring any of those, it must condition on `tier != "direct"`. Not a bug today, latent risk.

### B9. Skill files won't get uninstalled cleanly
**File:** `commands/uninit.md` Step 6

Only removes `pipeline-debug.md` and `gateway-audit.md`. If more skills are ever added to `skills/`, they'll be left behind. Should auto-discover like `copy_skills` does.

### B10. `stats.md` references registry but doesn't load it
**File:** `commands/stats.md`

When `$ARGUMENTS` is empty, falls back to `active.json`. If neither active nor argument, errors out. Could instead read the registry index and pick the most recently-updated feature. Minor.

### B11. Resume doesn't carry `previous_verdict` when resuming IMPLEMENTING with retry_count > 0
**File:** `commands/resume.md`

Step 3's "Paused during IMPLEMENTING" path spawns the orchestrator and tells it "Resume from STEP 5". But step 5 of the orchestrator reads `extra.previous_verdict` from the implementer spawn — when /resume goes through the orchestrator, the orchestrator does its own STEP 5 logic which reads from frontmatter, so it's actually OK. **Not a bug, but the chain is fragile.** Worth documenting.

### B12. `eval_runner.py` only covers a few cases
**Memory note:** Only `planner/case_01` through `case_03`, `implementer/case_01`, `reviewer/case_01-02`, `orchestrator/case_01`. No coverage for:
- Direct tier (newly added)
- /debug agent (newly added)
- Parallel reviewer subagents (newly added)
- adr_agent, srs_agent
- Any subagent (linter, unit_tester, query_analyzer, security_scanner, performance_reviewer, quality_reviewer)

Adding evals isn't a bug per se but the current coverage gives false confidence.

---

## 🟡 Drift (documentation/state inconsistencies)

### D1. README claims "4-pass security scan" — still true, but doesn't mention parallel reviewer
README's "Quality gates" section is out of date with recent commits. Mention:
- Parallel specialist reviews (security + performance + quality)
- Direct tier complexity classification
- `/debug` command
- `/stats` for token observability

### D2. README "Cross-feature memory" section is accurate but `memory.md` template is broken
See B2.

### D3. `planner.md` Direct Mode says "skip CONTEXT INPUTS + Phases 1–5" but the real phase numbering goes 1–4 in the active Process section (no Phase 5 anymore). The phase list itself is correct, just the inline reference is loose.

### D4. `/feature` command pipeline diagram doesn't show Direct tier or `/debug`
**File:** `commands/feature.md` line 36
> PLANNING → AWAITING_CONFIRMATION → IMPLEMENTING → REVIEWING → SRS_UPDATE → COMPLETE

Direct tier still goes through every stage, just with lighter agents. But a developer reading this won't know `tier` exists.

### D5. `pipeline-states.md` doesn't mention `tier` as a state-adjacent attribute
The frontmatter field is documented in `feature_spec.md` template but not surfaced in pipeline-states.md. New developers won't discover it.

### D6. `prompts/sub-agent-pipeline.md` doesn't mention performance/quality reviewers
This file documents the IMPLEMENTING-stage sub-agents (linter, unit_tester, query_analyzer). Performance & quality run during REVIEWING and are documented in `reviewer.md` — OK, but cross-referencing would help readers understand the full sub-agent landscape.

### D7. `decisions.md` (live, in `.stangent/`) is committed to the stangent repo itself
**File:** `.stangent/decisions.md`

Stangent shouldn't have its own `.stangent/` directory unless it's dogfooding. If dogfooding, it should be an example. If not, it should be `.gitignore`d. Currently neither documented nor consistent.

### D8. Agent versions are inconsistent and never bumped together
- orchestrator: 1.1.0
- planner: 1.1.0
- implementer: 1.1.0
- reviewer: 1.1.0 (just had a major refactor — should be 1.2.0)
- srs_agent: 1.0.0
- adr_agent: 1.1.0
- debug: 1.0.0
- subagents/linter: 1.0.0
- subagents/unit_tester: 1.1.0
- subagents/query_analyzer: 1.1.0
- subagents/security_scanner: 1.0.0
- subagents/performance_reviewer: 1.0.0 (new)
- subagents/quality_reviewer: 1.0.0 (new)

No version policy. Pipeline records `*_agent_version` in feature frontmatter — but with no consistent bumping, the field is decorative.

---

## 🟢 Gaps (missing capabilities)

### G1. No `/test` standalone command
The `unit_tester` subagent exists but is only invoked inside the implementer's sub-agent pipeline. Common dev need: "I changed something, just re-run the tests." Currently requires `/implement FEAT-XXX` which does much more.

**Suggested:** `/test [FEAT-XXX]` — invoke unit_tester directly against either the active feature or a specific feature. Returns Test Report only.

### G2. No `/refactor` command
The-startup has this. Stangent users have to use `/plan` + `/implement` which is overkill for a behaviour-preserving refactor. Could be a thin wrapper that:
- Sets `tier = direct` automatically
- Requires baseline tests pass before any change
- Auto-rolls back if tests fail after change

### G3. No cost/token aggregation in observer output
Observer logs `chars` per file read. `/stats` shows char totals but no token estimate beyond `chars/4`. Could:
- Pull actual token counts from API response headers (when using Anthropic API directly)
- Show $ estimate based on configured model pricing
- Show per-stage cost breakdown (planner: $X, implementer: $Y, etc.)

### G4. No per-tier model assignment
`config.json` has per-agent model assignment (`models.planner`, `models.reviewer`, etc.) — but no way to say "use Haiku for Direct tier planner, Sonnet for Standard." Direct tier on Sonnet still uses 30–50k tokens. Putting it on Haiku could be 3–5x cheaper without quality loss for tiny fixes.

**Suggested:** `models.planner_direct`, `models.reviewer_direct` overrides. Fall back to `models.planner` / `models.reviewer` if absent.

### G5. No spec diff visualisation for `/refine`
When `/refine` bumps `spec_version` from 1 → 2, the developer never sees what changed in the spec. They see a "revision summary" from the planner but not a real diff. Should:
- Save previous spec snapshot before revision
- On confirmation prompt, show a unified diff of the planner-owned sections
- Lets developer catch unexpected scope expansion

### G6. No fixture codebase for evals
Evals run agents against synthetic inputs but with no real codebase to read. Agents that depend on grep/glob/read will short-circuit because there's nothing to find. Need a small fixture project (maybe `evals/fixtures/sample_app/`) that agents can be pointed at during eval runs.

### G7. No CI integration
Stangent has no GitHub Actions / pre-push hooks. A useful integration:
- On PR open: run `/doctor` and post results as a check
- On PR open: run any evals affected by changed agent files
- On merge: auto-run `/srs --dry-run` and alert if SRS would change

### G8. No prompt versioning / changelog
Agent files have `version: X.Y.Z` in frontmatter but no `## Changelog` section. When debugging a failure, you can't tell "did this prompt change since the last successful run?" Should add changelog entries inline, or maintain `agents/CHANGELOG.md`.

### G9. No "shadow run" mode
Can't test a feature spec without actually running the full pipeline. Should support `/feature --shadow <description>`:
- Runs planner only
- Outputs the spec to stdout
- Doesn't create a feature file, branch, or contract
- Useful for "what would stangent do with this request?"

### G10. No partial-AC implementation
Spec might have 5 ACs. Sometimes you want to ship 3 and defer 2. Currently the implementer must complete all ACs or the unit_tester fails. Should support:
- `## Deferred ACs` section
- Implementer can mark an AC as `[d] deferred` (different from `[x] done` or `[ ]` pending)
- Reviewer accepts deferred ACs if explicitly listed
- SRS records them as known gaps

### G11. No automatic spec drift detection post-merge
After a feature is COMPLETE and merged, nothing checks whether the merged code still matches the spec. Drift happens. Could add `/audit FEAT-XXX` that re-runs the reviewer on the current state of the merged branch (or main).

### G12. No staleness check for ADRs
ADRs accumulate forever. Some become irrelevant (technology migration, design pivot). Should:
- Track `last_referenced` date on each ADR (updated when planner cites it)
- `/adr stale` command lists ADRs not referenced in N days
- Suggest review or deprecation

### G13. No cross-project memory
`memory.md` is per-project. A developer's preferences ("no emojis in commit messages", "always test edge cases") are likely the same across all their projects. Could have a `~/.stangent/global_memory.md` that's read in addition to project-local memory.

### G14. No retry budget visible in dashboard
`/status` shows retry_count but not which stage retried, or how close to escalation. Would be useful to see "FEAT-007 — IMPLEMENTING (2/3 retries used, last failure: REVIEW_CRITICAL)" at a glance.

### G15. No prompt linter
Agent prompts are markdown but follow a structure (frontmatter fields, ROLE, CONTEXT INPUTS, CONSTRAINTS, OUT OF BOUNDS, PROCESS, OUTPUT CONTRACT, ESCALATION). No automated check that every agent file has these. Easy to introduce a typo or forget a section.

### G16. No `--yes` flag on commands that pause for confirmation
Power users running stangent in a loop or automation can't pass `--yes` to auto-confirm. Every confirmation point requires interactive input. Limits CI/automation use cases.

### G17. Section ownership isn't enforced by gateway
Gateway enforces paths but not which section of a file an agent writes to. Implementer could overwrite `## Scope` and gateway wouldn't block. Currently relies on agents reading `section-ownership.md` and obeying. A real enforcement would parse the feature file, identify which section is being edited, and check the editor's role.

### G18. No notification mechanism for PAUSED features
If a feature pauses for developer input and the developer walks away, no notification. Could:
- Write to `~/.stangent/inbox.json` for the developer to check
- Optional: ping a webhook / Slack on PAUSED
- `/status --unread` shows features that paused since you last ran `/status`

### G19. Failure patterns don't influence model selection
If `memory.md` shows that "FEAT-007 area always fails review on first try", the orchestrator could pre-emptively use a stronger model for the implementer on FEAT-007-area features. Currently failure patterns are surfaced to the planner only.

### G20. No way to clone a feature
Sometimes you want a new feature that's very similar to an existing one (different module, same pattern). Currently you describe it from scratch. Could have `/feature --like FEAT-003 <new description>` that copies relevant spec sections as a starting point.

---

## 🔵 Concepts (architectural observations worth thinking through)

### C1. Tiers should probably be 3, not 2
the-startup uses Direct / Incremental / Factory. Stangent has Direct / Standard. A middle tier for "single feature, but with multiple sub-components" could split implementation into phases the developer confirms between. Today this is either "all-at-once Standard" or you manually split into multiple features.

Worth waiting for real demand before adding — over-tiering is its own complexity.

### C2. The pipeline assumes solo developer
Multi-developer notice exists in orchestrator STEP 0d but it's advisory only. There's no merge-conflict prediction, no "FEAT-007 depends on files Bob is editing in FEAT-008", no lock coordination beyond the registry lock file. For teams, this matters more.

Defer: stangent's stated audience is the solo / small-team developer; explicit multi-dev support might be a different product.

### C3. Memory is write-only-by-orchestrator
The protocol says only orchestrator writes memory.md. But agents observe things during their work that would be valuable. The current pattern (agents surface observations → orchestrator consolidates on COMPLETE) means:
- Failed runs never write memory (orchestrator only writes on COMPLETE/ESCALATED/ABANDONED)
- Mid-run insights are lost
- Patterns from interrupted/paused features go uncaptured

Could open up a "scratch memory" that any agent can write to, and orchestrator consolidates on terminal states.

### C4. The reviewer's parallel spawning has no failure handling
If `performance_reviewer` crashes, the main reviewer just gets empty findings — silently treated as PASS. Should:
- Each parallel agent returns a structured status (OK / SKIPPED / ERROR)
- Main reviewer records subagent failures explicitly in the verdict
- Treat ERROR as a MAJOR finding ("performance review could not complete")

### C5. Direct tier might be too aggressive about skipping ADRs
The planner's D1 step only checks ADRs whose titles substring-match the request. A request like "fix broken animation" could touch state management — but it won't match "ADR-003 State Management — Riverpod" by title. Could miss binding constraints.

Mitigation: also check ADR consequences for path patterns (e.g. ADR-003 says "all screens use ConsumerWidget" — if the request mentions a screen, check that ADR). More involved but safer.

### C6. The "confidence score" system is decorative
Planner Confidence, Implementer Confidence, Reviewer Confidence all get written but nothing acts on them. The handoff validator surfaces a WARN if score < threshold but doesn't block. Could:
- Promote low confidence to "request developer review before proceeding"
- Use confidence to decide whether to spawn extra review specialists (low planner confidence → spawn extra cross-stack scan even on Direct tier)
- Trend confidence over time — if planner confidence is dropping across features, something's eroding

### C7. The context_cache.md is committed but project-specific
Cache references `git_hash` so it's correct, but it bloats commits. Probably should be in `.gitignore`. Each developer rebuilds locally.

### C8. The gateway logs aren't pruned
`gateway_audit.jsonl` grows forever. After a year of use it's huge. Need a rotation policy or pruning command (`/cleanup --logs`).

### C9. The "Direct tier auto-confirm" decision was avoided
For Direct tier features, the orchestrator still pauses at AWAITING_CONFIRMATION. For trivial bug fixes this means an extra round-trip. Could add `config.pipeline.direct_auto_confirm: true` opt-in setting.

Counter-argument: even trivial changes deserve a glance before code starts being written. Default opt-in is risky.

### C10. There's no way to "approve and continue" without re-reading the spec
The developer at AWAITING_CONFIRMATION reads the spec, types "yes", and the orchestrator proceeds. But the spec is in markdown form, displayed inline — large specs are unwieldy in the terminal. Could:
- Open the spec file in the developer's `$EDITOR` for review
- Provide a `/preview FEAT-XXX` command that renders the spec nicely
- Show diff against previous spec_version if /refine

### C11. The `--soft` / `--hard` uninit dichotomy is right but the threshold isn't clear
`--soft` keeps `.stangent/` (feature data) but removes tooling. `--hard` removes everything. What about "I want to keep my decisions.md and SRS.md but throw away features?" No middle option.

### C12. No way to "fork" a feature
If FEAT-005 is mostly right but you want to explore an alternative implementation, you can't say `/fork FEAT-005`. You'd `/abandon` and `/feature` from scratch. A fork would keep planner context, generate a sibling FEAT-005b, and let you compare.

---

## What an ideal agentic dev workflow looks like (and where stangent stands)

| Capability | Ideal | Stangent today |
|---|---|---|
| Tiered execution by complexity | 3+ tiers, auto-classify | ✅ Direct / Standard (just added) |
| Cost transparency | $ and tokens per stage | 🟡 Observer logs chars; no $ |
| Memory & learning | Improves over time | 🟡 memory.md exists, not always written |
| Parallel specialist agents | Yes | ✅ Just added (reviewer) |
| Real evals | High coverage, CI-gated | 🟡 Only a few cases, no CI |
| Spec/code traceability | Bidirectional | ✅ Feature file as anchor |
| Reversibility | Easy rollback of any change | ✅ Branch-per-feature |
| Cross-feature awareness | Dependency-aware | ✅ depends_on + dep check |
| Observability | Full session replay | 🟡 Observer + audit log, no UI |
| Multi-model use | Right model for right task | 🟡 Per-agent, not per-tier |
| Branch / git hygiene | Auto-clean stale | ✅ /cleanup exists |
| Hard guardrails | Filesystem-level enforcement | ✅ Gateway hook |
| Onboarding | One command setup | ✅ init.py |
| Skill discovery | Searchable / self-documenting | ❌ No /help, no skill listing |
| Notification on idle | Inbox/ping | ❌ Missing |
| Spec drift detection | Post-merge audit | ❌ Missing |
| Cross-project knowledge | Global memory | ❌ Per-project only |
| Self-improving prompts | Prompts learn from failures | ❌ Static prompts |
| Pluggable specialists | Easy to add review dimensions | ✅ Subagent pattern |
| Recovery from API failure | Backoff + retry | 🟡 Single failure halts |
| Human approval points | Explicit, skippable in auto | 🟡 No `--yes` flag |

---

## Recommended priority order

If picking next work, this ordering balances impact, effort, and risk:

1. **B1, B2** — fix section-ownership and memory template (5 min each, real bugs)
2. **B4, B5** — update reviewer description and OUTPUT CONTRACT (5 min, docs accuracy)
3. **B6, B9** — make /uninit auto-discover commands and skills (15 min, prevents future drift)
4. **G3 (token estimate in /stats)** — actually compute tokens from chars * 0.25 + record subagent spawn cost separately (1 hour, high user value)
5. **G1 (`/test` command)** — thin wrapper around unit_tester (1 hour)
6. **G4 (per-tier model)** — `models.planner_direct` config field (2 hours, big cost saving)
7. **C4 (parallel reviewer failure handling)** — silent subagent failures are dangerous (2 hours)
8. **G15 (prompt linter)** — script that validates every agent .md has required sections (2 hours, prevents future drift)
9. **G6 (fixture codebase for evals)** — required before adding any new evals (1 day)
10. **G2 (`/refactor`)** — could be a thin wrapper too (half day)

Defer indefinitely: G7 (CI), G10 (partial AC), G18 (notifications), C2 (multi-dev), C12 (fork) — high effort, unclear demand.

---

## Inconsistencies summary table

| ID | File | Issue | Fix effort |
|---|---|---|---|
| B1 | prompts/section-ownership.md | Missing 5 sections | 5 min |
| B2 | templates/memory.md | Column count mismatch | 5 min |
| B3 | agents/reviewer.md | Stale phase ref in confidence | 2 min |
| B4 | agents/reviewer.md | OUTPUT CONTRACT incomplete | 2 min |
| B5 | agents/reviewer.md | Frontmatter desc stale | 2 min |
| B6 | commands/uninit.md | Hardcoded command list | 15 min |
| B7 | commands/doctor.md | (already fixed) | — |
| B8 | scripts/validate_handoff.py | No tier awareness | 30 min |
| B9 | commands/uninit.md | Hardcoded skill list | 5 min |
| B10 | commands/stats.md | No fallback to registry | 10 min |
| B12 | evals/ | Coverage gaps | Days |
| D1 | README.md | Stale capability list | 15 min |
| D4 | commands/feature.md | Diagram doesn't show tier | 5 min |
| D8 | agents/*.md | Version policy missing | 30 min (write policy) |

---

## Resolution log (2026-05-24 follow-up session)

Below is what was fixed in the session after the audit, in commit `9d2ccef` and follow-ups.

### Resolved bugs
- ✅ **B1** — section-ownership.md now lists all 7 missing sections including new Performance/Quality Review and Context Checkpoint
- ✅ **B2** — memory.md template includes the Replans column
- ✅ **B3** — reviewer confidence references corrected (Phase 4 for cross-stack)
- ✅ **B4** — reviewer OUTPUT CONTRACT lists Performance/Quality Review blocks
- ✅ **B5** — reviewer frontmatter description rewritten for parallel specialists
- ✅ **B6** — uninit + init_scaffold.py replaced hardcoded command list with `.stangent/.installed.json` manifest
- ✅ **B8** — validate_handoff.py reads `tier` from frontmatter, skips Codebase Context + Planner Confidence checks for Direct tier
- ✅ **B9** — skill removal also uses the manifest
- ✅ **B10** — /stats falls back to registry's most-recently-updated feature when no active.json
- ✅ **B7** — already fixed in `ee0b209`

### Resolved drift
- ✅ **D1** — README adds Complexity tiers, Parallel specialist reviews, Observability sections
- ✅ **D4** — /feature pipeline diagram now shows TIER CLASSIFICATION step
- ✅ **D5** — pipeline-states.md documents `tier` attribute
- ✅ **D6** — sub-agent-pipeline.md cross-references the reviewer's parallel subagents
- ✅ **D8** — agents/VERSIONING.md written; planner bumped to 1.2.0, reviewer to 1.2.0

### Resolved concepts / gaps
- ✅ **C4** — parallel reviewer subagent failures now tracked (EMPTY/ERROR statuses, subagent_failures flag in Reviewer Confidence, MAJOR finding emitted on failure)
- ✅ **G1** — `/test [FEAT-XXX|--all]` command added
- ✅ **G3** — /stats now prints per-agent token estimate and $ cost (Anthropic pricing) with disclaimer
- ✅ **G4** — config.template.json has `*_direct` model overrides; orchestrator STEP 1g.5 resolves them
- ✅ **G14** — /status shows tier and last failure_type per active feature
- ✅ **G15** — scripts/prompt_lint.py validates required sections + frontmatter; passes against all 13 agents
- ✅ **G16** — `--yes` flag on /feature (auto-confirm) propagates to orchestrator STEP 4
- ✅ **G18** — `/inbox` command for actionable features only
- ✅ **G5** — `/preview <FEAT-ID> --diff` shows spec rendering with optional git-based diff against prior spec_version
- ✅ **G9** — `/feature --shadow <description>` runs planner only, prints spec, writes nothing
- ✅ **C8** — `/cleanup --logs` rotates files >10MB, keeps 3 rotated copies

### Skipped (deliberate)
These were considered and deferred because the effort/value ratio is wrong for the current stangent maturity:

- **B12** (eval coverage expansion) — needs G6 fixture codebase first
- **G6** (fixture codebase) — full day of work; not blocking anything urgent
- **G7** (CI integration) — deserves its own design discussion
- **G2** (`/refactor`) — can already be done via `/plan` + `/implement`; thin wrapper is low-value
- **G8** (changelog per agent) — VERSIONING.md serves this for now
- **G10** (partial AC) — significant model change
- **G11** (post-merge drift detection) — needs decision on storage of "merged at" state
- **G12** (ADR staleness) — needs decision on how `last_referenced` is tracked
- **G13** (cross-project memory) — architectural decision, may be future scope
- **G17** (section ownership enforcement by gateway) — significant gateway rewrite; current honor system works
- **G19** (failure pattern → model selection) — architectural, premature
- **G20** (clone feature) — wait for demand
- **C1** (3 tiers) — wait for demand
- **C2** (multi-dev) — out of scope
- **C5** (Direct tier ADR safety) — needs design thought
- **C6** (confidence scores → actions) — needs design thought
- **C7** (context_cache gitignore) — already gitignored
- **C9** (Direct auto-confirm) — `--yes` flag covers the automation case
- **C10** (spec viewer) — addressed via `/preview`
- **C11** (third uninit mode) — wait for demand
- **C12** (fork feature) — wait for demand

### Session count
- **23 files changed** across consistency fixes
- **5 new files** added (VERSIONING.md, prompt_lint.py, test.md, inbox.md, preview.md)
- All audited bugs that didn't require major architectural decisions are now fixed
- All audited drift items are now fixed
- All audited capability gaps that are < 1 day of work and don't require new architectural decisions are now fixed


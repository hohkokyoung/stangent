---
phase: 4
name: agents
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 2, cli-conventions: 3, code-graph: 6, impact-report: 5, ticket-bundle: 2, bug-proof: 1 }
provides: { task-plan: 1, agent-run: 1 }
---

# Phase 4 · Agents

**Grilled 2026-11-16 (D343–D349); build shape D350. Minimal loop built with fakes and fresh-checked 2026-11-16 (D351, D352), committed; D345 run 2026-11-16 on the 12 unit-test bugs: both sides fixed 12 of 12, the bundle for 27% more tokens, so it failed its bar and the fixer's default is now the ticket, the test and the finder's files (D357).**

One fix agent per ticket, inside a loop run by code (D343). The agent plans and edits; code runs every check;
a failed check gives one retry, then the ticket goes back to a person. No task splitter and no parallel subagents
until a measured ticket needs them (D49: the graph's split failed its safety bar). The first thing the loop is used
for is a measurement: does the ticket bundle help a fix (D345)?

## The loop (D343, D344, D346, D347, D348)

```
stangent fix start <KEY>      worktree .stangent/fix/<KEY>/worktree, branch stangent/fix-<KEY>,  code
                              the bug proof's test committed first ("test: reproduce <KEY>")
stangent fix prepare <KEY>    the fixer's input: ticket (+ bundle unless --context ticket),        code
                              the failing test and its failure, the limits
stangent-fixer                plans (task-plan v1) and edits in the worktree                     AI, multi-turn
stangent fix check <KEY>      plan, files, build, tests, test reach; one retry with the failures  code
stangent fix report <KEY>     report.md + agent-run.json: why it stopped, each check, the branch  code
plugin/scripts/fix-run.sh     drives the above through headless Claude Code (D162); /stangent:fix calls it
```

**Test first (D346).** The minimal loop takes bugs only, and its first-step test is the bug proof's (`ticket prove`
must have `reproduced`). No reproduced proof → the report says "no failing test" and hands the plan-less ticket to the
developer. A test writer for features waits for D345's result (D350).

**The plan (D344, `task-plan` v1).** Each change: `file`, `point` (graph id at the base commit), `why` (one of: a
ticket quote, an FSD section id with a quote from the bundle, a failing test's name), and `reach` (filled by code from
`graph impact --file`, not by the agent).

**Checks, in order (D343, D346):**
1. Plan: every point exists in the base graph; every quote is word for word (`Quotes.Normalize`, as `fsd review`);
   a test name is a real test.
2. Files: every file the diff touches (base..worktree, the proof test excluded) is a planned file or in a planned
   file's reach; others are listed, and fail the check.
3. Build: the targets' build commands.
4. Tests: the proof's `--verify` logic on the worktree: `passed` required; `new-failures`, `same-failure`,
   `different-failure`, `not-compiled`, `not-run` fail.
5. Test reach: the graph rebuilt in the worktree (incremental); the proof test must be in `graph impact`'s `tests`
   group of at least one planned point. A graph that can't be rebuilt marks this "not checked", never "passed".

**Caps (D348).** Sonnet 5.5, medium effort. Each fixer call: 30 turns and at most $1, and never more than what's left
of the ticket's $3 (the proof's cost counts). The run: `--max-cost`. Hitting a cap stops the ticket: what's on the
branch stays, the report says "cap hit".

**Never:** a push, a PR, an edit outside the worktree, an edit to the proof test (a changed test fails check 4 via
`testChanged`), `.stangent/` or `.git/`.

## The first measurement (D345)

Sonarr's 17 bugs whose saved test fails before the developer's fix and passes after it (`phases/3-tickets.md`, the
D105 re-score). Base: each fix's parent. Side A: `--context ticket` (ticket, code, failing test). Side B: `--context
bundle`. Scored by code: test green, other tests green, files against the developer's fix, tokens; a fresh agent marks
each green diff clean or symptom patch (SONB-18). Bar, frozen: B ≥ A + 2 of 17 with tokens within 10%, or the same
count for ≥ 10% fewer tokens; else the fixer gets only the finder's files and the failing test. Before the run: re-check
the 17 saved tests still fail at each parent and pass at each fix (free). About $8–34, `--max-cost 35`, after
2026-11-18, the owner's OK first.

## Build steps

1. Schemas `task-plan.v1.json`, `agent-run.v1.json`; `docs/fix.md`; `fix` in `stangent.yaml`'s components.
2. `fix start`: worktree, branch, the proof test copied and committed; refuses a dirty base or a missing proof.
3. `fix prepare` / `fix check` with the five checks and one retry; `stangent-fixer` agent file and its limits.
4. `fix report`; telemetry from `agent-run` (D21).
5. `fix-run.sh` (reusing `ticket-run.sh`'s multi-turn driver and `cost-guard.sh`); `/stangent:fix`.
6. Tests with fakes (a fake Claude Code, a small .NET fixture repo with a failing test), all free.
7. Narrow fresh check (D158).
8. The D345 harness: `benchmark/tickets/fix_test.sh` + scorer, free to write; the run is paid.

## Progress

- 2026-11-16: grilled (D343–D349), plan written (D350). Next: step 1.
- 2026-11-16: **steps 1–8 built, free, fakes only** (D351, D352; `docs/fix.md`). Schemas `fixer-output` v1 (the agent's answer), `task-plan` v1, `agent-run` v1; `fix` component and `fix:` settings (`callBudget`, `ticketUsd`, `localFiles`); `stangent fix start | prepare | check | report` (`src/Stangent.Tickets/Fix/`); the `stangent-fixer` agent and its instructions; `plugin/scripts/fix-run.sh` (shares `ai-run.sh`'s new `result_with_rounds` with `ticket-run.sh`) and `/stangent:fix`; the run's stop on the state module (`fix-run`). Tests: `FixLoopTests` (23, a real git repo and worktree on `tests/fixtures/fix-shop`, a fake runner, canned graphs) and `FixRunDriverTests` (6, fake Claude Code and stangent). The fixture really compiles: its proof test fails on the bug and passes once fixed (checked by hand with `dotnet test`, not in the suite). The D345 harness `benchmark/tickets/fix_test.sh` (`recheck` free, `run` paid, `score`) and `fix_score.py` written; the scorer smoke-tested on made-up records, the harness not run. Then **step 7, the narrow fresh check** (D158; a fresh Sonnet agent, ~144K tokens): 8 faults (3 medium, 5 low), all fixed (D352): other tests must really be checked, tests are never reach, the scorer compares only bugs both sides ran and counts green only with test reach passed, exact test-name matching, the recheck's rule, the harness's stdin and stops. Build 0 warnings, 1904 of 1904 tests, `phases check` clean. **Committed 2026-11-16 (feat(fix): Phase 4's minimal fix loop).** **Next:** D345's run after 2026-11-18 (paid, asked first).
- 2026-11-16: **D345's free recheck run:** 17 of 22 reproduced bugs still qualify (the same 17 as D105; SONB-1, 2, 11, 14, 30 drop out). Its first real `fix start` found a bug the fakes hid: an untracked `stangent.yaml` or page output made the base dirty. Fixed (D353): only changed tracked files are dirt; untracked ones are named in a warning. Then the paid run's first calls (stopped after SONB-12, ~$0.20) found three more wiring faults (D354): `--agent` dropped StructuredOutput, the read-only hook refused the fixer's edits, and a proof test under an ignored folder couldn't be committed. All fixed; the run resumes with them. `89e0b16` worked on a real call (SONB-12 side A: `fixed`, the developer's one-line fix). One more (D355): a repo that git-ignores `stangent.yaml` made the stop's commit fail; fixed. A fifth (D356): the Playwright bugs' app scripts weren't in the worktree; `localFiles` now takes folders. Those 5 bugs still can't pass check 4 (no full run, D352), so D345 is scored on the 12 unit-test bugs; a full-run equivalent for e2e targets is owed.
- 2026-11-16: **D345 run** (recheck session, stangent `89e0b16`, $2.20 recorded): A (ticket) 12 of 12 for 674K tokens, B (bundle) 12 of 12 for 854K (+27%); the bar fails whatever the clean-or-symptom judging finds (D357). `fix prepare --context files` (ticket, test, the finder's files) is the default. On all 6 bugs where the developer changed several files, both sides changed one. **Next:** the fresh clean-or-symptom judging of the 24 green fixes (asked first), and a harder bug set before any context can be shown to help.

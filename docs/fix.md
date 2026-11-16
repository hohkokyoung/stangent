# The fix loop (Phase 4)

One fix agent per bug, inside a loop run by code (D343). The agent plans and edits; code runs every check; a failed
check gives one retry, then the ticket goes back to a person. Every fix is on its own branch in its own git worktree,
and nothing is ever pushed (D347). Plan: `phases/4-agents.md`. Decisions: D343–D354.

```
plugin/scripts/fix-run.sh --max-cost 3 --context bundle SONB-14
started   SONB-14  start
retry     SONB-14  check        ← a check failed; the fixer's one retry
fixed     SONB-14  check
fixed     SONB-14  fix
```

`/stangent:fix <KEY>` runs the same script. The ticket needs a **reproduced bug proof** first (`ticket-run.sh --prove`,
`docs/tickets.md`): the loop is bugs only, and its first-step test is the proof's (D346, D350).

## The steps

| command | what it does | who |
|---|---|---|
| `stangent fix start <KEY> [--base REF] [--replace]` | worktree `.stangent/fix/<KEY>/worktree`, branch `stangent/fix-<KEY>` from HEAD (or `--base`); the proof's test copied from the checkout and committed first, `test: reproduce <KEY>` | code |
| `stangent fix prepare <KEY> --context ticket\|bundle` | the fixer's input (`work/input.md`) and its limits | code |
| fixer (`stangent-fixer`'s instructions; started without `--agent`, D354) | edits in the worktree, returns its plan (`fixer-output` v1) as structured output | AI, multi-turn |
| `stangent fix check <KEY> --usage <result>` | the five checks in order; one retry with the failures | code |
| `stangent fix report <KEY>` | `report.md` and `agent-run.json` (`agent-run` v1); the run's telemetry event | code |

`fix start` refuses a ticket with no reproduced proof (`no_failing_test`: recorded, so `fix report` says "no failing
test" and the ticket goes to the developer), a checkout whose tracked files changed beyond the proof's test and
stangent's own folder (`dirty_base`; untracked files never reach the worktree, so they're only named in a warning, D353), and a ticket that already has a fix run unless `--replace` (which removes that worktree and branch).
It also copies into the worktree, git-ignored: `stangent.yaml` if the repo doesn't track it, the proof (with the
repo's path in its build arguments moved to the worktree, so `--verify` builds the worktree's code) and the graph (so
the rebuild in check 5 is incremental).

**`--context` (D345).** `ticket`: the ticket (summary, description, comments, the bug's fields), the code (the
worktree) and the failing test with its failure. `bundle`: the same plus the ticket bundle as `docs/tickets.md`
describes it (starting points, pages, FSD sections, impact, attachments). Only `bundle` allows an FSD reason.

## The plan (D344)

Each change names **where**, **why** and **reach**:

```json
{ "changes": [ { "file": "src/NzbDrone.Core/Tv/SeriesService.cs", "point": "SeriesService.UpdateSeries",
    "why": { "kind": "ticket", "quote": "monitoring resets after I edit the series" } } ] }
```

- `point`: a graph id, or a name declared in that file; code resolves it in the graph at the base commit. Left out
  only for a new file.
- `why`: exactly one of `ticket` (a quote), `fsd` (a bundle section id and a quote from it), `test` (a test's name).
- `reach`: never the agent's. Code fills it from the base graph, as `graph impact --file` walks it: every file of
  code (not tests) the change's file reaches. Stored in `.stangent/fix/<KEY>/task-plan.json` (`task-plan` v1).

A fixer that can't fix it here makes no edits and gives `gaveUp` with why: the report says so and the ticket goes back.

## The checks, in order (D343, D346, D350)

The first that fails stops the round; its output goes back to the fixer for the one retry, with its edits still in the
worktree. Each check's full output is in `.stangent/fix/<KEY>/checks/round-<n>-<i>-<name>.txt`.

| # | check | passes when |
|---|---|---|
| 1 | plan | it matches `fixer-output` v1; every point is in the base graph in its file; every quote is word for word (`Quotes.InText`: whitespace collapsed, at least 4 words or a whole line); every test name is the proof's test or a test in the graph; no planned file holds existing tests (D352) |
| 2 | files | every file edited since the test's commit (the proof test, `.stangent/` and a copied `stangent.yaml` left out) is a planned file or in a planned file's reach (tests are never reach, D352); an edited proof test fails it |
| 3 | build | the proof target's build command succeeds in the worktree (skipped for a target with none) |
| 4 | tests | the proof's `--verify` (D105) in the worktree gives `passed`: the test passes and a full run of its target shows no other test newly failing; other tests not checked (the full run timed out or gave none) fail it too (D352). `new-failures`, `same-failure`, `different-failure`, `not-compiled`, `not-run` and an edited test fail it |
| 5 | test reach | the graph, rebuilt in the worktree, puts the proof's test among the tests reaching a planned point. A graph that can't be rebuilt (or a plan of only new files) is **not checked**, never passed |

All pass → `fixed` (with "test reach not checked" when check 5 couldn't tell). Round 2 fails → `check-failed`.

## Caps (D348)

Sonnet 5.5 at medium effort (`fix-run.sh`'s defaults; `--model`/`--effort` change them for one run, D351). Each fixer
call: `fix.callBudget` (30 turns, $1), and never more than what's left of `fix.ticketUsd` ($3), the proof's cost
counted. A call that stops at its cap stops the ticket (`cap-hit`). The run's hard stop is `--max-cost` (cost-guard.sh).

```yaml
fix:
  callBudget: { usd: 1.0, turns: 30 }
  ticketUsd: 3.0
components:
  fix: true        # off: every `stangent fix` command refuses
```

## The limits

The fixer starts in the worktree with its input on stdin (it never reads stangent's folder) and may only: read and edit
inside the worktree; never `.stangent/`, `.git/`, the proof's test or a copied `stangent.yaml` (deny rules win); run
the proof target's build and test commands. Paths are absolute (D89). Its plan comes back as structured output.
Whatever it edited is committed on the branch when the ticket stops (`fix: <KEY>` when fixed, `wip: <KEY> (stopped:
…)` otherwise), so the branch holds it. `fix-run.sh` checks the developer's checkout is unchanged after the run (T.5).

## The report (D349)

`.stangent/fix/<KEY>/report.md`: why it stopped, the branch and base, what the fixer was given, the model, the cost
against the ticket's cap, the plan (where · why · reaches), each check, the last failed check's output, and the
edited files (any outside the plan's reach marked). `agent-run.json` holds the same as data. Stop reasons: `fixed`,
`no-failing-test`, `gave-up`, `cap-hit`, `check-failed`, `in-progress`.

**Telemetry (D21).** Each `fix check` event carries its call's model, tokens and cost (so `guard_estimate "fix check"`
reads a per-call cost); the `fix report` event carries the run's model, the fixer's tokens and the number of checks,
not the cost again.

## The first measurement (D345)

`benchmark/tickets/fix_test.sh` runs both sides on Sonarr's 17 bugs and `benchmark/tickets/fix_score.py` scores them;
see `phases/4-agents.md`. Written, not yet run (paid; after 2026-11-18, the owner's OK first).

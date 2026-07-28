# SKILL: regression

## Purpose
Governs how finished tests are **recorded** so they can be re-run later and read
as a regression signal. Scope: registering a case in `.claude/tests/cases/`,
recording a run outcome, quarantining a flaky case, revising a baseline. This
skill is runner-agnostic and stack-agnostic — it never says how to drive an app
or which assertions to make. That is the job of the paired execution skill
(`flutter-skill`, `maestro`, `playwright`, or a bare test runner), which this
skill composes with rather than replaces.

Does NOT cover: writing test code, choosing test cases, driving a device or
browser, or deciding whether a task is done.

## Rules

1. **Every case that passes gets registered, before the task is finalized.** A
   green run that leaves no record is work that will be redone.
2. **Allocate ids with the allocator, never by hand:**
   `sh .claude/py .claude/hooks/lib/test_registry.py next-id`. Copy
   `.claude/templates/test-case.md` to `.claude/tests/cases/<id>-<slug>.md`.
3. **`command` must be exactly what you ran** — from the repo root,
   non-interactive, repo-relative paths only. If you cannot state the command
   you ran, you cannot register the case; say so in `## Test results` instead of
   registering something you have not executed.
4. **Fill `expect` from what the run actually reported**, in the runner's own
   words. An assertion phrased in words the runner never emits cannot be checked
   by the next run.
5. **Record outcomes through the CLI, never by editing frontmatter:**
   `test_registry.py record <id> --result pass|fail|error|skipped --evidence a.png,b.png`.
   The CLI writes `last_run` and `history` and cannot touch `expect` — that
   separation is what keeps a green run falsifiable.
6. **Never edit `expect` to make a failing case pass.** If the new behaviour is
   correct, that is a *revision*: change `expect`, bump `revision`, and add a
   line to the case's `## Revision log` saying what changed and why. A silent
   re-baseline turns every later green run into a claim nobody can check.
7. **A case you cannot make deterministic is `quarantined`, not deleted**, with
   `flake_reason` stating what is unstable. Deleting it hides the gap; leaving
   it `active` and flaky trains everyone to ignore the gate.
8. **Validate before finishing:** `test_registry.py validate` must exit 0. Fix
   what it reports — it is checking the determinism contract, not style.
9. **Every `covers` entry must be real.** Point at DoD bullets, ADR ids and task
   ids that exist. `covers` is what lets the `### Coverage` table in
   `## Test results` be reconstructed later; a fabricated entry is worse than an
   empty list.
10. **Regenerate the index** with `test_registry.py index` after adding or
    retiring cases.

## Patterns

### Register a case after a passing run
```bash
ID=$(sh .claude/py .claude/hooks/lib/test_registry.py next-id)   # -> TC-004
cp .claude/templates/test-case.md ".claude/tests/cases/$ID-login-happy.md"
# fill in: id, title, kind, surface, runner, command, expect, covers, fixtures
sh .claude/py .claude/hooks/lib/test_registry.py validate
sh .claude/py .claude/hooks/lib/test_registry.py record "$ID" --result pass \
  --evidence .claude/state/evidence/$ID/dashboard.png
sh .claude/py .claude/hooks/lib/test_registry.py index
```

### A filled case, front matter only
```yaml
id: TC-004
title: signing in with valid credentials lands on the dashboard
kind: happy
surface: e2e-mobile
runner: flutter-skill
status: active
revision: 1
command: "flutter-skill run .flutter-skill/auth/TC-004.yaml --device ios-17-iphone15"
artifact: ".flutter-skill/auth/TC-004.yaml"
expect:
  exit_code: 0
  assertions:
    - "Dashboard is visible"
    - "no element with text 'Invalid credentials'"
covers:
  requirements: ["user can sign in with email and password"]
  adrs: [ADR-004]
  tasks: [FEAT-003/T3]
fixtures: "seeded user qa+login@example.test / fixed password from .env.test"
```

### Revise a baseline (behaviour changed on purpose)
```
1. Confirm the new behaviour is correct — cite the ADR or DoD bullet that says so.
2. Edit `expect` to the new observable outcome.
3. revision: 1 -> 2
4. Append to ## Revision log: "r2 <date> — dashboard copy changed by ADR-009;
   assertion updated from 'Welcome back' to 'Your day'."
5. validate, then record the run.
```

### Read the gate
```bash
sh .claude/py .claude/hooks/lib/test_registry.py regressions
# REGRESSION TC-004 ... — was passing, now fail     → something broke; investigate
# FAILING     TC-011 ... — has never passed          → known gap; not a regression
```

## Anti-patterns

- Registering a case whose `command` you did not run — the registry then asserts
  coverage that has never been executed once.
- Editing `expect` in the same change that records a failing run. That is a
  re-baseline wearing a bug fix's clothes.
- Hand-editing `last_run` or `history`. Use `record`; hand edits desynchronise
  the history that `regressions` reads.
- Deleting a flaky case instead of quarantining it.
- One case covering the whole flow. Split happy / boundary / failure — a single
  fat case tells you something broke but not what.
- Registering cases only at the end of a run, from memory. Register each one as
  it passes, while the exact command is still in front of you.
- Leaving `covers` empty because it is tedious. An untraceable case survives
  exactly until someone wonders whether it still matters.

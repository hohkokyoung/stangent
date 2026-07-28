# Test case registry

The durable record of what has actually been tested in this project, and the
thing `/agentic-regress` re-runs to prove none of it broke.

One case is one file: `cases/TC-NNN-slug.md`, YAML frontmatter plus prose. There
is no central manifest — a manifest is a second source of truth that has to
agree with the files, and it is the one file every branch edits, so two features
adding a case conflict every time. `INDEX.md` is generated *from* the cases for
humans; nothing reads it back.

```
.claude/tests/
  README.md            this file
  INDEX.md             generated — sh .claude/py .claude/hooks/lib/test_registry.py index
  cases/
    TC-001-login-happy.md
    TC-002-login-wrong-password.md
```

## Why this is not just the test files

The project's own tests already exist — `test/`, `spec/`, `.maestro/`,
`e2e/`. The registry does not replace them and does not store test code. It
records, per case:

- **what to run**, exactly, so a rerun is a rerun and not a re-derivation;
- **what passing means**, so a failure is a fact rather than a judgement call;
- **what it is evidence for** — the DoD bullet, ADR or task it covers;
- **whether it has ever passed**, so a red bar can be read correctly.

That last one is the whole point. A suite that is red tells you nothing about
whether something *broke*. A case that passed on Tuesday and fails today is a
regression; a case that has never passed is a gap. `/agentic-regress` separates
them, and it can only do that because the history lives somewhere durable.

## Runner-agnostic on purpose

A case names a `runner` but the registry never interprets it. `command` is a
shell command; whether it invokes `flutter-skill`, `maestro`, `playwright`,
`pytest`, `go test` or a bash script is the runner skill's business. That is
what lets one gate sit over a Flutter app, a FastAPI service and a React
frontend in the same monorepo, and what lets a new stack be adopted by writing a
skill rather than by changing this directory.

## The determinism contract

`test_registry.py validate` enforces these, and refuses the case otherwise:

1. **`command` is non-interactive and repo-relative.** No absolute path into a
   home directory — it runs on one machine and passes vacuously everywhere else.
2. **Nothing is read from the clock or a random source at run time.** No
   `$(date …)`, `uuidgen`, `$RANDOM`. If the case needs a timestamp or an id,
   pin it and describe it in `fixtures`.
3. **`expect` carries at least one of `exit_code` or `assertions`.** A case with
   nothing to compare against cannot fail, so it cannot catch a regression — it
   is coverage theatre and validation treats it as an error.
4. **A `quarantined` case states a `flake_reason`.** Excluding a case from the
   gate is allowed; excluding it silently is an untracked coverage hole.

## The rule that makes the gate mean anything

**`expect` is never edited to make a failing run pass.**

Recording a result and changing the baseline are deliberately different
operations: `record` only ever writes `last_run` and `history`, and cannot touch
`expect`. Changing `expect` is a hand edit that bumps `revision` and explains
itself in the case's `## Revision log`. If that rule is broken, every green run
afterwards is unfalsifiable, and the registry is worse than having none —
it *asserts* coverage it no longer has.

When a case fails and the new behaviour is correct, that is a revision, and it
gets written down as one.

## Lifecycle

```
active  →  quarantined  →  active
        →  retired
```

- `active` — in the gate. A regression here fails `/agentic-regress`.
- `quarantined` — flaky, excluded from the gate, `flake_reason` required. Still
  listed, so the hole stays visible.
- `retired` — the behaviour is gone. Kept for history; never deleted, because a
  deleted case and a case that never existed look identical in six months.

## Commands

```bash
sh .claude/py .claude/hooks/lib/test_registry.py next-id
sh .claude/py .claude/hooks/lib/test_registry.py validate
sh .claude/py .claude/hooks/lib/test_registry.py list --status active
sh .claude/py .claude/hooks/lib/test_registry.py regressions
sh .claude/py .claude/hooks/lib/test_registry.py index
```

Registering a case by hand: copy `.claude/templates/test-case.md` to
`cases/<id>-<slug>.md`, fill it in, run `validate`. In the normal flow the
tester does this for you — see `.claude/skills/regression/SKILL.md`.

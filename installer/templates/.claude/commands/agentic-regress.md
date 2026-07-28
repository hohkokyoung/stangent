---
description: Re-run every registered test case in .claude/tests/ and report regressions — cases that used to pass and no longer do.
argument-hint: "[run|status|validate|quarantine <id> <reason>|retire <id>]"
---

# /agentic-regress

The regression gate. Re-runs the cases recorded in `.claude/tests/cases/` and
separates **regressions** (this passed before, it does not now) from **known
gaps** (this has never passed). A red suite alone cannot make that distinction;
the registry's history is what makes it possible.

Runner-agnostic by construction: each case carries its own `command`, so a
project mixing Flutter, a Python service and a React frontend runs one gate.

## Subcommands

| Command | What it does |
|---|---|
| `/agentic-regress` or `run` | Run every `active` case, record results, report |
| `/agentic-regress status` | Report from recorded history — runs nothing |
| `/agentic-regress validate` | Check the registry against the determinism contract |
| `/agentic-regress quarantine <id> <reason>` | Take a flaky case out of the gate, on the record |
| `/agentic-regress retire <id>` | Mark a case obsolete; keeps it for history |

---

## run (default)

### Step 1 — validate first

```bash
sh .claude/py .claude/hooks/lib/test_registry.py validate
```

If it exits non-zero, print the problems and **stop**. Do not run a registry
that does not satisfy its own contract — a case with no `expect` will "pass"
and inflate the count.

### Step 2 — enumerate

```bash
sh .claude/py .claude/hooks/lib/test_registry.py list --status active
```

One JSON object per line. If there are none:

```
[agentic-regress] No active cases registered. Run /agentic-test init to
bootstrap, or let a tester task register cases as it verifies them.
```

Then stop — report zero cases, never "all green". They are not the same claim
and only one of them is true.

### Step 3 — run each case

For each case, in id order:

1. Print `→ <id> <title>`.
2. Run its `command` via Bash from the repo root.
3. Decide the result:
   - `pass` — exit code matches `expect.exit_code` (default 0) **and** every
     string in `expect.assertions` appears in the output.
   - `fail` — the command ran and the expectation did not hold.
   - `error` — the command could not run at all (missing binary, no device, no
     server). Distinct from `fail` on purpose: an unrunnable case is an
     environment problem, and reporting it as a failure sends someone to debug
     the wrong thing.
4. Record it:
   ```bash
   sh .claude/py .claude/hooks/lib/test_registry.py record <id> --result <result> \
     --evidence <paths,if,any>
   ```

**Do not modify `expect` at any point in this step**, whatever the outcome. If a
case fails because the behaviour changed on purpose, that is a revision and it
is a separate, deliberate edit — see the `regression` skill. Re-baselining
inside the run that observed the failure is how a gate stops meaning anything.

Run cases sequentially unless every case declares itself isolated. Shared
fixtures plus parallel runs is a flake generator.

### Step 4 — report

```bash
sh .claude/py .claude/hooks/lib/test_registry.py regressions
sh .claude/py .claude/hooks/lib/test_registry.py index
```

Print:

```
Regression run — <n> active case(s)
-----------------------------------
✓ TC-001  login with valid credentials reaches the dashboard
✓ TC-002  wrong password shows an inline error
✗ TC-004  checkout applies the promo code
          REGRESSION — passed 2026-07-21 (a1b2c3d), now fail
          expected: "Total RM 38.00"   got: "Total RM 42.00"
! TC-007  offline queue drains on reconnect
          ERROR — no device attached; not a code failure
~ TC-009  push notification arrives          (quarantined: APNs sandbox flaky)

1 regression, 1 error, 6 passing, 1 quarantined
```

Exit the report with the regression count stated plainly. **A run with any
regression is a failed run** — say so, do not soften it with the passing count.

### Step 5 — on a regression

Do not fix it here. State, for each regression:
- the case id and what it asserts,
- the commit it last passed at,
- the diff between expected and observed.

Then offer `/agentic-debug` with that case as the repro. Fixing inside the gate
run mixes the measurement with the change to the thing being measured.

---

## status

Read-only. Runs `regressions` and `index` against recorded history and prints
the same report shape, marking every result as "as of <last run date>". Use it
to see where things stood without paying for a full run.

---

## validate

```bash
sh .claude/py .claude/hooks/lib/test_registry.py validate
```

Print each problem with its file. These are contract violations — a
non-reproducible command, a missing `expect`, a quarantine with no stated
reason — not style notes.

---

## quarantine <id> <reason>

Edit the case file: `status: quarantined`, add `flake_reason: "<reason>"`, and
append a line to its `## Revision log`. Then `validate` and `index`.

Quarantine is for a case that is genuinely unstable, not for one that is
failing. A failing case is a finding; quarantining it to get a green bar is
deleting the finding.

---

## retire <id>

Edit the case file: `status: retired`, and append to `## Revision log` what
replaced it or which feature was removed. Then `index`.

Never delete a case file. A deleted case and a case that never existed are
indistinguishable later, which is exactly when someone asks whether that
behaviour was ever covered.

---

## CI

The gate is just the recorded commands, so CI does not need an agent:

```bash
sh .claude/py .claude/hooks/lib/test_registry.py validate || exit 1
sh .claude/py .claude/hooks/lib/test_registry.py list --status active \
  | python3 -c 'import sys,json;[print(json.loads(l)["command"]) for l in sys.stdin]' \
  | while read -r cmd; do eval "$cmd" || exit 1; done
```

That loop checks exit codes only. The assertion strings in `expect` are checked
by `/agentic-regress run`, so CI is the weaker of the two — use it as a fast
guard, not as the reason to skip the full gate.

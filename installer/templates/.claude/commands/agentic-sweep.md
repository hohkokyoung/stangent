---
description: Exhaustive review — every in-scope file read against every rule, in computed batches
argument-hint: "<ui|security|audit> [dir:<path>] [--max-chars N]"
---

# /agentic-sweep

The ordinary reviews are *sampling* reviews: one pass, one agent, reading what it
judges worth reading. That is fine for a quick read of recent work and it is why
they are cheap. They cannot tell you what they did not look at.

This is the exhaustive form. The file list is computed, split into batches, and
every batch is dispatched — so coverage is arithmetic (`164/164 files × 11/11
rules`) rather than a claim. Use it when you need a rule actually closed, not
sampled: before a release, after a token migration, or when repeated reviews keep
surfacing findings in code nobody touched.

It costs more than `/agentic-review-ui` and it terminates. Say so to the
developer before starting, with the real batch count from step 2.

## Lanes

One algorithm, three lanes. Everything below is identical except this table.

| lane | agent | checklist | reports under | default patterns |
|---|---|---|---|---|
| `ui` | design-critic | `docs/design/DESIGN-SPEC.md` §13 | `ui-review/` | UI source only |
| `security` | security-reviewer | `.claude/agents/security-reviewer.md` (8 categories) | `security-review/` | **all** source |
| `audit` | auditor | `.claude/agents/auditor.md` | `audit/` | **all** source |

`ui` sweeps the UI surface; `security` and `audit` sweep everything, because an
IDOR or a swallowed exception is not confined to widget files. That makes them
proportionally more expensive — say so before dispatching.

There is deliberately no `design` lane: the architect's seven dimensions are
questions about a design (*who owns this entity, what breaks at 100×*), not rules
applied to files, so batching source at it would be cargo-culting. Likewise no PR
lane — a diff is already a bounded, stable population.

## Preconditions

- The lane's checklist must exist and yield items. For `ui` that means
  `docs/design/DESIGN-SPEC.md` (authored by `/agentic-design`); without it there
  is nothing to sweep against, so stop and say so.
- Pass `dir:<path>` to narrow scope.

## Algorithm

Batching is computed by `sweep_plan.py`, not by you. You never choose which files
to read or which to skip — that choice is the thing this command exists to
remove.

### Step 1 — Establish context

```bash
LANE='<ui|security|audit>'   # from the argument
DIR='<ui-review|security-review|audit>'   # per the lane table
REVIEW_ID="SWEEP-$(date +%Y%m%d-%H%M%S)"
mkdir -p .claude/state/$DIR/$REVIEW_ID/batches
printf '%s' "$REVIEW_ID" > .claude/state/current_run.txt
printf '%s' '<the lane's agent>' > .claude/state/current_role.txt
printf '%s' '<resolved_model>' > .claude/state/current_model.txt
```

Resolve `<resolved_model>` from `models.<the lane's agent>` in `.agentic.yml`
(fall back to `models.default`, then the session default) and pass it explicitly
at invocation. A sweep dispatches one agent per batch and is the most expensive
command here, so an unarmed `current_model.txt` would leave every one of those
calls logged with a null model — exactly the run whose cost you most need to
attribute.

**Do not choose file patterns.** `sweep_plan.py` reads the globs `/agentic-index`
already detected into `.claude/state/project.yml`, so the sweep covers the same
file universe as the retrieval index. It prints which it used and whether they
were detected or a fallback; if it says fallback, run `/agentic-index` rather than
passing patterns by hand — a guessed pattern that misses a language still reports
complete coverage of what it did match.

The `ui` lane is narrowed by **scope**, not by pattern: pass the UI source
directory (`dir:mobile/lib`, `dir:src/`). `security` and `audit` take the whole
repo, since an IDOR is not confined to the view layer.

### Step 2 — Compute the plan, and show the cost

```bash
python3 .claude/hooks/lib/sweep_plan.py plan '<scope>' --pattern '<pat>' [--max-chars N]
```

Print the summary line (`N files, NNNKB, B batches`) and **confirm with the
developer before dispatching**, because the cost is roughly linear in batch
count. If they want it cheaper, re-run with a larger `--max-chars`: fewer, bigger
batches pay the fixed per-batch overhead fewer times. Do not reduce *scope* to
save money without saying so — a narrower sweep that still reports "complete" is
the failure this command exists to prevent.

### Step 3 — Dispatch every batch

For each batch in the plan, in order, invoke **the lane's agent** with:

- the batch's exact file list — it reviews **those files and no others**
- `review_id`, the batch index, and the spec
- an instruction to write findings to
  `.claude/state/$DIR/$REVIEW_ID/batches/b<NN>.md`

Each batch checks **every** checklist item against **every** file it was given. This is
the inversion that makes the sweep work: a normal review walks rules and hunts
for sites, so the sites it does not think to look at are invisible. Here the
files are fixed and the rules are applied to them, so a rule cannot quietly skip
a file.

A batch that finds nothing still writes its file, containing `no findings`. An
absent file means the batch never ran, and step 5 treats it as such.

Per batch, the report is just findings — no Coverage table, no verdict. Those are
whole-sweep properties and belong in the merge.

### Step 4 — Merge

Read every `batches/b*.md` and write
`.claude/state/$DIR/$REVIEW_ID/findings.md` using
the lane's report template (`ui-critique.md` for `ui`; the lane agent's own report
shape otherwise):

- **Group by rule, not by batch.** One finding per rule with the complete site
  list gathered from every batch. Batch boundaries are an implementation detail
  of how the reading was scheduled and must not appear in the output.
- **Deduplicate on `rule + file:line`.** The same violation seen from two angles
  is one finding.
- **Consistency scan** — the one thing no single batch can see. Diff what the
  batches observed about shared components: the same component styled two ways in
  two directories, one colour expressed as two values, a spacing value used once.
  Batches saw parts; this is where the whole is checked.
- **Coverage table** — one row per checklist item, as always. Because every file was
  read against every rule, `inspected` is the sweep's file count, not a sample:
  write `164 of 164 files`.

### Step 5 — Verify coverage, then citations

```bash
python3 .claude/hooks/lib/sweep_plan.py verify '<scope>' \
  .claude/state/$DIR/$REVIEW_ID/batches --pattern '<pat>' [--max-chars N]
${PYEXE:-python3} .claude/hooks/lib/verify_clears.py \
  .claude/state/$DIR/$REVIEW_ID/findings.md --cwd . \
  --checklist '<the lane's checklist path>'
```

The first is the claim this command sells: it fails when a batch never reported,
so the sweep cannot describe itself as exhaustive when it was not. Use **exactly
the same scope, patterns and `--max-chars`** as step 2 — a different plan verifies
a different sweep and would pass while covering less.

The second is the ordinary evidence check. Both outputs go above the findings.

Then clear the state:

```bash
python3 .claude/hooks/lib/state.py clear
```

### Step 6 — Present

Lead with coverage — `164/164 files, 43/43 batches, 11/11 rules` — then the
verdict and findings. If step 5 reported missing batches, say plainly that the
sweep is **incomplete and its coverage claim does not hold**, and name the
unexamined files. A partial sweep reads exactly like a complete one; only that
line distinguishes them.

## Limits — state these with the result

The sweep guarantees **nothing was skipped**. It does not guarantee nothing was
missed: a critic reading a batch can still overlook a violation inside it, the
same way a human reviewer can. Coverage is not detection.

And some rules are not in the source at any batch size — contrast, focus rings and
rendered states for `ui`; anything requiring a running system or real traffic for
`security`. Those stay `unverified` here and need a rendered or runtime pass. Do
not let a complete-coverage line imply they were checked.

## Constraints

- Never skip a batch, reorder them, or merge two to save a pass.
- Never substitute your own file list for the computed one.
- Read-only. This command writes reports; it never edits code.
- Findings flow to `/agentic-remediate <review_id>` like any other review.

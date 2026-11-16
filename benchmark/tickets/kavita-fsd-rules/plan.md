> **Superseded 2026-11-16 (D361–D364), not run.** Two things changed it before any bug was planted. (1) The reproducer
> never reads an FSD rule: `TicketBundle.Markdown` prints a section's id, title and link states, not its text. (2) The
> owner moved the judge from the FSD to the **truth map**: the PM's rulings on the FSD review's findings
> (`rulings.yaml`), so a ticket is backed, contradicted or unclear, and unclear stops with a Jira question. The
> measurement now waits for the truth map to be built. When it is, this plan is redone with planted right and wrong
> tickets (the ticket asks for what a ruling says, or against it), scored by code: does `ticket prove` call each one
> right, and does a wrong ticket stop before any fix. The planter (approved, about $3–6) runs then, not now.
> `flow-anchor/` holds the free checks behind D361 and D362.

# Does the FSD help when the rule is only there? (D358's open question)

Frozen 2026-11-16, before any bug exists. Nothing below changes after the planter's output is written.

## The question

D358: in the fix loop the failing test carries a business rule to the fixer. The reproducer (`ticket prove`) gets the
bundle with its FSD sections (`forProof`). Left open: when the rule is stated only in the FSD (not in the ticket),
(a) does the reproducer's test check it, with the FSD sections against without, and (b) does the fixer
(`fix-run.sh`, default `--context files`, no FSD) then follow it?

## The bugs (planted, blind)

A fresh Opus agent (the planter) reads Kavita's planted FSD and key, which this session never reads
(`benchmark/fsd/kavita/`), and plants 4–5 bugs in the pinned Kavita clone (`2d225367f0`). Each bug:

- **The rule:** a sentence the FSD states, that the code at the pin follows, that a unit test in
  `Kavita.Services.Tests` (or another existing unit-test project) can check, and that is none of the FSD's 30 planted
  items (those are where the FSD and the code differ on purpose).
- **The plant:** one small change that breaks something the ticket reports (the symptom) and also the rule, so that
  repairing the symptom alone does not bring the rule back.
- **The ticket:** what a user sees, written as a real reporter would; it never states or hints at the rule.
- **The half fix:** repairs the symptom, not the rule. It shows a plausible fix can miss the rule.
- **The rule test:** a unit test that passes at the pin, fails on the plant, and fails on the half fix.
- **The sections:** the FSD section that states the rule plus up to two nearby sections, in FSD order, as the bundle
  would carry them.

Stimulus (`public/`, read by scripts and applied, not studied): the plant patch, the ticket in the replay layout, the
sections. Key (`sealed/`, read only by scoring code): the rule tests, the half fixes, the planter's notes. Hashes in
`sealed/SHA256SUMS`. The planter checks every claim above by running the tests and records the output.

## The runs

Per bug, from the plant commit (a local branch in the benchmark clone, never pushed):

1. `ticket pull` (replay) → `ticket points` (the file finder) → `ticket bundle`. Shared by both arms.
2. **Arm A:** `ticket prove` with the bundle as it is (Kavita has no FSD links, so no sections).
   **Arm B:** the same with the planter's sections added to the bundle's `sections` (the best case: the right section
   arrives; whether links would deliver it is a separate question).
3. `fix-run.sh --context files` once on each arm's reproduced test.

## What's measured (by code)

- **(a) The test checks the rule:** the arm's proof test fails on the plant (`reproduced`), passes at the pin, and
  fails on the half fix.
- **(b) The fix follows the rule:** the sealed rule test passes on the fixer's result.
- Also recorded: the fixer's own result (its test passes), tokens and dollars per step.

## The bar (frozen)

With n = number of bugs the planter delivers (4 or 5) and only bugs whose test was reproduced in both arms counted:

- **The FSD helps the reproducer** if arm B's test checks the rule in at least n−1 bugs and in at least 2 more than
  arm A. Then the bundle's FSD sections earn their place in `ticket prove`, and getting the right section linked
  (Kavita has no links today) is the work that matters next.
- **It doesn't** if arm B checks the rule in no more than 1 more bug than arm A. Then reading the FSD in the
  reproducer doesn't carry a rule into the test, and D358's answer needs a change (a prompt that asks for the
  section's rules, or the FSD reaching the fixer).
- Anything between is talked through with the owner, not called either way.
- **D358's "the test carries the rule" holds** if every fix whose test checks the rule follows it. A fix that follows
  the rule when its test doesn't check it is counted and reported, not part of the bar.

Small on purpose: 4–5 bugs of one repo on an AI-written FSD. It can show a large effect or none, nothing finer.

## Cost (asked before each paid step)

- Planter: one Opus agent, about 200–300K tokens (approved by the owner 2026-11-16).
- Finder: 4–5 × up to $1 (`findBudget`). Proof: 8–10 × up to $1 (`proveBudget`). Fix: 8–10 runs, D345 averaged about
  $0.25–1. In all about $10–25 under `--max-cost`.

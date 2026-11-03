---
phase: 2c
name: spec
status: needs review
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3, impact-report: 5, reference-page: 1, source-document: 1, trace-links: 1, ticket-bundle: 2, bug-proof: 1 }
provides: {}
---

# Phase 2c · The structured spec

**Grilled (D22) 2026-11-03; build order approved by the owner 2026-11-03; being built.** The design is D99, placed before Phase 4 by D109.
This file holds what is settled for the build; D99 keeps the reasons.

## What it adds
The FSD stays the record. On top of it: requirements with stable ids, each quoting the FSD word for word, linked to reference page
statements and through them to code. The BA approves each one. Then a ticket can be matched to an approved requirement, which gives
"bug, per R-0042" or "looks intended (R-0042)".

## Settled
- **Everything in D99:** the items, ids, files, several documents and `fsd reconcile`, Jira and as-built sources, BA approval by tick boxes.
- **Matching a ticket (owner, 2026-11-02):** candidates by code through links; an AI picks one or none, with quotes code checks; a fresh
  AI checks without seeing the pick; **a person always confirms.** Until then it reads "suggested: R-0042".
- **A requirement no statement covers (owner, 2026-11-02):** it can still match, after linked ones, labelled "no code found for this rule".
- **Scoring the match (owner, 2026-11-02):** a key written by a fresh agent and frozen before anything is built, on the planted FSD:
  tickets that clearly fit a requirement and tickets that clearly don't. It's our own test set, so a real FSD stays the judge (D56).
- **Bars for the match (owner, 2026-11-02):** on 30 tickets (15 with a requirement and code, 5 with a requirement and no code, 10 with none):
  at least 90% of suggestions right; at least 8 of 10 "none"; at least 14 of 20 found. If either of the first two fails, matching ships off;
  if only the third fails, it ships on and the report says so.
- **A fix against an approved requirement (owner, 2026-11-02):** the agent stops and asks; the rule changes through the BA first. The diff check is Phase 4's.
- **Recheck triggers (owner, 2026-11-02):** a stale page's links are judged again, here. The PR list of requirements with no test waits for Phases 5–6. None after a failed proof.
- **Phase 3 when this lands (owner, 2026-11-03):** `ticket match` as its own step; `ticket-bundle` v3; `bug-proof` v2 with the verdict; drafts may link a requirement.
- **Adoption (owner, 2026-11-03):** `/stangent:adopt` and a driver script (names not final) run the existing steps in order and resume; no separate audit (replaces D14's).

## Build order (owner approved, 2026-11-03)
The core comes first and is scored before the rest, since matching can ship off if it fails its bars.
0. A new branch from `main`. A fresh agent writes and freezes the matching key, with no view of how matching works.
1. **Extraction:** requirements from the planted FSD, with quotes checked by code, every sentence cited or `background`, and a fresh checker.
2. **Links:** requirement ↔ page statement, scored on the 30-item key (the bar below).
3. **Matching:** `ticket match`, scored on the frozen key.
4. **Approval:** tick boxes in a spec-only PR, listing each section's background sentences too; `decided_by` from git; the setting benchmarks use to skip it.
5. **Phase 3's follow-up** (D20): `ticket-bundle` v3, `bug-proof` v2, drafts; Phase 3 goes `needs review`, then its tests pass again.
6. **Staying current:** a stale page's links judged again; code carries items forward across imports, so ids and approvals hold; `fsd reconcile` and its CI run; one pass over a document for clashes between sections.
7. **Other sources:** Jira change requests; as-built drafts.
8. **`/stangent:adopt`,** with the driver script.

## Done when (from D99)
- [ ] Requirement-level links find at least 27 of the planted FSD's 30 items, with false alarms at or below 30% (2b: 27/30, 31.3%).
- [ ] Every item cites word-for-word quotes, checked by code; every FSD sentence is cited or marked `background`.
- [ ] A fresh AI checks each item says no more than its quotes.
- [ ] Matching passes its bars on the frozen key, or ships off as its rule says.
- [ ] A real-FSD run (D56), when a project with one is found; until then the report says it's owed.
- [ ] Benchmark runs skip BA approval only through a setting off by default, and every result made with it says so (D99).
- [ ] End-of-phase report, its claims checked by a fresh agent (D61).

## Contracts
Provides a new contract beside `source-document` (name not decided). Phase 3 goes `needs review` when it lands (D20).

### Needs review 2026-11-04 · `code-graph` v3 → v4 (overhaul 1.1, 1.2; D115)
v4 added `opens-screen` lines, kept apart from the walked lines, and `meta.dirtyFiles`. Follow-up: confirm nothing here depends on v3, re-run this phase's tests, then move `consumes` to `code-graph: 4`.

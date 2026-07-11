---
run_id: <run-id>
title: "<one-line feature name>"
status: deferred                 # deferred | resumed | shipped
deferred_on: <UTC date>
resumed_on: null
branch: <feat/run-id, or null if never created>
last_commit: <short SHA + subject on that branch, or null>
blocked_on: "external: <dependency>"      # what the world is missing — see templates/blocker-reference.md
resume_when: "<observable condition, e.g. 'backend /health returns 200 on staging'>"
---

# <run-id> — <feature title>

## Goal

<!-- copied from _overview.md ## Goal — why this feature exists, in the user's terms -->

## What shipped (done tasks)

<!-- one bullet per done task: id — intent — load-bearing lines from its ## Decisions log -->
- t1 — <intent>. <notable decisions, files touched, contracts introduced>

## What's half-done / remaining

<!-- one bullet per deferred task: id — intent — how far it got -->
- t3 — <intent> (design written, no code)
- t4 — <intent> (never started)

## Why it stopped

<!-- the external blocker in plain words; include what was tried, if anything -->

## Resume checklist

<!-- everything a future reader needs to pick this up cold -->
- [ ] <resume_when condition is true>
- [ ] `git switch <branch>`; rebase onto the base branch if it drifted
- [ ] Re-run `/agentic-index` — code and skills may have moved on while parked
- [ ] `/agentic-resume <run-id>` to unfreeze the tasks
- [ ] Re-read the `## Decisions log` of done tasks before continuing

## Context that will be lost otherwise

<!-- anything NOT recoverable from code or task files: verbal agreements,
     external tickets, where credentials will come from, gotchas discovered
     mid-build, half-validated hypotheses -->

---
id: TC-000
title: <one line, present tense, states the observable outcome>
kind: happy               # happy | boundary | failure | regression
surface: e2e-mobile       # e2e-mobile | e2e-web | e2e-desktop | api | integration | unit
runner: <skill name>      # flutter-skill | maestro | playwright | pytest | jest | go_test | ...
status: active            # active | quarantined | retired
revision: 1               # bump whenever `expect` changes; say why in ## Revision log

# Exactly what to run, from the repo root, with no interactive input.
# Repo-relative paths only — an absolute path runs on one machine.
command: ""

# The artifact this case executes, if the command points at one. Empty when the
# command is self-contained (e.g. a single `pytest -k` selector).
artifact: ""

# What a passing run must produce. At least one of exit_code / assertions.
# These are the regression gate: never edit them to make a red run go green.
expect:
  exit_code: 0
  assertions:
    - "<observable fact, in the words the runner reports it>"

# Traceability. Every entry is something this case is the evidence for.
covers:
  requirements: []        # free-text DoD bullets this case exercises
  adrs: []                # e.g. [ADR-004]
  tasks: []               # e.g. [FEAT-003/T3]

# How to reach the precondition. "none" if the case is self-seeding.
# Anything the command would otherwise pick up from the clock, the network or a
# previous run belongs here, pinned.
fixtures: "none"

# flake_reason: required when status is quarantined — why it cannot be trusted.

# Written by the tester / /agentic-regress. Do not hand-edit.
# last_run: {at: ..., result: pass, commit: ..., evidence: [...]}
# history: []
---

<!--
The comments above are authoring guidance and disappear the first time
`test_registry.py record` rewrites the frontmatter — YAML round-tripping drops
them. Everything that needs to survive belongs in the prose below.
-->


# TC-000 — <title>

## Intent

<Why this case exists: the behaviour that would be broken if it started failing.
One paragraph. A reader who has never seen the feature should be able to judge
whether a failure here matters.>

## Steps

1. <what the command does, in order, in plain language>
2. <...>

## Determinism notes

<What makes this reproducible: pinned fixtures, seeded data, fixed clock, a
device profile. If anything here is fragile, say so — a case whose fragility is
written down can be quarantined honestly instead of silently re-baselined.>

## Revision log

- r1 <date> — registered.

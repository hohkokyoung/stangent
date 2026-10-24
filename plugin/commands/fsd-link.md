---
description: Link an imported FSD to the reference pages (Phase 2b) and list the gaps, conflicts and undocumented items.
argument-hint: "<doc id> [section id]"
allowed-tools: Bash(stangent fsd:*), Bash(stangent trace:*), Bash(*spec-run.sh:*), Task
---

Link the agreed FSD to the reference pages with stangent (D53, D62). Code decides every step; you only run the
subagent each step names. Never judge a link yourself, and never edit `specs/trace.yaml` except where the user asks you
to set a `decision`.

**A whole document** (`$ARGUMENTS` is a document id): run the batch driver, then the code-only steps:

```bash
${CLAUDE_PLUGIN_ROOT}/scripts/spec-run.sh --jobs 4 --fsd <doc id>
stangent fsd undocumented
stangent trace check
```

**One section** (`<doc id> <section id>`), here in this session:

1. Run `stangent fsd prepare "<doc>" "<section>"` and read `result.next`.
2. While `result.next.action` isn't `none`: start the subagent in `result.next.agent` (`stangent-candidate-finder`,
   `stangent-link-judge` or `stangent-agree-checker`) with exactly `result.next.prompt`, wait, then run
   `stangent fsd check "<doc>" "<section>"` and read `result.next` again (exit 1 means a retry or a failure; keep
   following `result.next`).

Then report from `stangent trace check`: counts by state, and list the gaps, conflicts and undocumented items for the
section(s), each with its two quotes. "Agree" links whose second check says `confirmed` can be approved in bulk with
`stangent trace approve <doc> <chapter>`; everything else is reviewed one by one by editing `decision` in a PR.

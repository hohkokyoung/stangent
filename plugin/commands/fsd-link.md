---
description: Link an imported FSD to the reference pages (Phase 2b) and list the gaps, conflicts and undocumented items.
argument-hint: "<doc id> [section id]"
allowed-tools: Bash(stangent fsd:*), Bash(stangent spec assemble:*), Bash(stangent trace:*), Bash(*spec-run.sh:*), Task
---

Link the agreed FSD to the reference pages with stangent (D53, D62). Code decides every step; you only run the
subagent each step names. Never judge a link yourself, and never edit `specs/trace.yaml` except where the user asks you
to set a `decision`.

**A whole document** (`$ARGUMENTS` is a document id): run the batch driver, then the code-only steps:

```bash
${CLAUDE_PLUGIN_ROOT}/scripts/spec-run.sh --jobs 4 --max-cost "$STANGENT_MAX_COST" --fsd <doc id>
stangent fsd undocumented
stangent trace check
```

**One section** (`<doc id> <section id>`), here in this session:

1. Run `stangent fsd prepare "<doc>" "<section>"` and read `result.next`.
   If a section fails with `no candidate page has been recorded yet`, `${CLAUDE_PLUGIN_ROOT}/scripts/spec-run.sh --for-fsd <doc>` writes only the pages this document needs. It costs money: say so first.
2. While `result.next.action` isn't `none`: run `stangent spec assemble --in-place <file>` for each `.stangent/` file the prompt says to read (a stored bundle holds only what's unique to it, D130), then start the subagent in `result.next.agent` (`stangent-candidate-finder`,
   `stangent-link-judge` or `stangent-agree-checker`) with exactly `result.next.prompt`, wait, then run
   `stangent fsd check "<doc>" "<section>"` and read `result.next` again (exit 1 means a retry or a failure; keep
   following `result.next`).

When `fsd.disprove` is on (D119), the batch driver also runs the disprove stage. For one section: `stangent fsd disprove prepare "<doc>" "<section>"`, the same
loop with `stangent-finding-disprover`, then `stangent fsd disprove check "<doc>" "<section>"`.

Then report from `stangent trace check`: counts by state, who each finding goes to (`goesTo`, `queues`), and list the gaps, conflicts and undocumented items for the
section(s), each with its two quotes. "Agree" links whose second check says `confirmed` can be approved in bulk with
`stangent trace approve <doc> <chapter>`; everything else is reviewed one by one by editing `decision` in a PR.

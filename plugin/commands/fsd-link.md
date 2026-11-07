---
description: Link an imported FSD to the reference pages (Phase 2b) and list the gaps, conflicts and undocumented items.
argument-hint: "<doc id> [section id]"
allowed-tools: Bash(stangent fsd:*), Bash(stangent trace:*), Bash(*spec-run.sh:*)
---

Link the agreed FSD to the reference pages with stangent (D53, D62). Code decides every step; the batch driver runs the
AI each step names. Never judge a link yourself, and never edit `specs/trace.yaml` except where the user asks you
to set a `decision`.

**Every AI step runs through the batch driver (D161, D162),** never as a subagent in this session: the driver gives each step the model and effort pinned in `stangent.yaml` and records them. If it stops with `ai_settings_missing`, tell the person to set `ai: { model: …, effort: … }` in `stangent.yaml` (or pass `--model` and `--effort` for one run) and stop: don't choose either yourself.

**A whole document** (`$ARGUMENTS` is a document id), or **one section** (`<doc id> <section id>`): run the batch driver, then the code-only steps:

```bash
${CLAUDE_PLUGIN_ROOT}/scripts/spec-run.sh --jobs 4 --max-cost "$STANGENT_MAX_COST" --fsd <doc id> [--only <section id>]
stangent fsd undocumented
stangent trace check
```

If a section fails with `no candidate page has been recorded yet`, `${CLAUDE_PLUGIN_ROOT}/scripts/spec-run.sh --max-cost "$STANGENT_MAX_COST" --for-fsd <doc>` writes only the pages this document needs. It costs money: say so first.

When `fsd.disprove` is on (D119), `--fsd` also runs the disprove stage; `--disprove <doc> [--only <section id>]` runs only that stage.

Then report from `stangent trace check`: counts by state, who each finding goes to (`goesTo`, `queues`), and list the gaps, conflicts and undocumented items for the
section(s), each with its two quotes. "Agree" links whose second check says `confirmed` can be approved in bulk with
`stangent trace approve <doc> <chapter>`; everything else is reviewed one by one by editing `decision` in a PR.

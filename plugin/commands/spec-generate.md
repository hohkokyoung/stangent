---
description: Generate stangent reference pages (Phase 2a) for a page, the stale pages, or all of them.
argument-hint: "<page id> | --for-fsd <doc> | --for-ticket <key> | --stale | --new | --all"
allowed-tools: Bash(stangent spec:*), Bash(*spec-run.sh:*)
---

Generate reference pages with stangent (D54, D60). Code decides every step; the batch driver runs the AI each step names.
Never write or edit a page yourself, and never judge a page yourself: the writer and a separate verifier do that (D61).

Pages are made when something needs them (I.1): `--for-fsd <doc>` for what a document's sections need, `--for-ticket <key>` for what a ticket's bundle says it's missing. `--stale` is written pages whose code changed. `--new` and `--all` make every page, which costs far more: only when the person asks for that.

**Every AI step runs through the batch driver (D161, D162),** never as a subagent in this session: the driver gives each step the model and effort pinned in `stangent.yaml` and records them. If it stops with `ai_settings_missing`, tell the person to set `ai: { model: …, effort: … }` in `stangent.yaml` (or pass `--model` and `--effort` for one run) and stop: don't choose either yourself.

Run the batch driver, which uses headless Claude Code and records tokens, time, model and effort per page, for one page or many:

```bash
${CLAUDE_PLUGIN_ROOT}/scripts/spec-run.sh --jobs 4 --max-cost "$STANGENT_MAX_COST" $ARGUMENTS
```

`STANGENT_MAX_COST` is the hard stop in dollars (`none` for no cap). If it isn't set, the driver stops at the first paid step and says so: ask the person for a cap and don't choose one yourself. Exit 5 means it stopped on the cap or a usage limit: report that line and the resume command.

It prints `<status>\t<page id>` per page: `current` (inputs unchanged, no AI ran), `recorded` or `written` (the page is in `specs/reference/`), `failed`, or `error` with why. Report the counts by status and list any `failed` pages with the reason from `specs/reference/failed.json`.

If a command fails with `no_graph`, run `stangent graph build` first. With `ai_disabled`, AI steps are off for this repo
(D24): say so and stop; `stangent spec stale` still works.

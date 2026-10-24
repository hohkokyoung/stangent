---
description: Generate stangent reference pages (Phase 2a) for a page, the stale pages, or all of them.
argument-hint: "<page id> | --stale | --all"
allowed-tools: Bash(stangent spec:*), Bash(*spec-run.sh:*), Task
---

Generate reference pages with stangent (D54, D60). Code decides every step; you only run the subagent each step names.
Never write or edit a page yourself, and never judge a page yourself: the writer and a separate verifier do that (D61).

**Many pages** (`--all`, `--stale`, or more than three ids): run the batch driver, which uses headless Claude Code and
records tokens and time per page:

```bash
${CLAUDE_PLUGIN_ROOT}/scripts/spec-run.sh --jobs 4 $ARGUMENTS
```

It prints `<status>\t<page id>` per page. Report the counts by status and list any `failed` pages with the reason from
`specs/reference/failed.json`.

**One page** (or a few), here in this session:

1. Run `stangent spec prepare "<page id>"`. It prints one JSON envelope; read `result.next`.
2. While `result.next.action` isn't `none`: start the subagent named in `result.next.agent`
   (`stangent-page-writer` or `stangent-page-verifier`) with exactly `result.next.prompt` as its prompt, and wait for it.
   Then run `stangent spec check "<page id>"` and read `result.next` again. `spec check` exits 1 when the writer must retry
   or the page failed; that's expected, keep following `result.next`.
3. When the action is `none`, report `result.status`: `current` (inputs unchanged, no AI ran), `recorded` (the page is in
   `specs/reference/`), or `failed` (with `result.errors`).

If a command fails with `no_graph`, run `stangent graph build` first. With `ai_disabled`, AI steps are off for this repo
(D24): say so and stop; `stangent spec stale` still works.

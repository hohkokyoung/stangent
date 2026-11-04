---
description: Ask how the app works today, answered from the reference pages with citations. Off unless spec.ask is on.
argument-hint: "<question>"
allowed-tools: Bash(*spec-run.sh:*)
---

Run the driver with the question as one argument:

```bash
"${CLAUDE_PLUGIN_ROOT}/scripts/spec-run.sh" --ask "$ARGUMENTS"
```

It prints a status line, then the result as one JSON object. Code picks the pages and checks every citation; you only report what comes back.

- `answered`: give `answer.sentences` in plain sentences, each with its `path:lines` from `cites[].lines`, and say what `answer.label` says: this is **as the pages describe it**. Pages are generated from the code, so they may have left things out, and a person hasn't necessarily read them.
- `not-covered`: say the pages don't cover this, and which pages were looked at, if any. Don't answer from your own knowledge or by reading code.
- `off`: `spec.ask` is off. Say so, and how to turn it on (`spec: { ask: true }` in `stangent.yaml`). It uses a paid AI call and hasn't been shown to help.

Never add to the answer. If `answer.notCited` is there, say which sentences have no lines to show, and why.

---
description: Show where an imported FSD stands, who has what, and the next thing to do.
argument-hint: "<doc id>"
allowed-tools: Bash(stangent fsd status:*)
---

Run `stangent fsd status $ARGUMENTS` in the current repo and report its result in plain sentences: the current version and
its verdict, any candidate waiting and doubtful sections, how many sections are uncovered, and how many findings wait on the
BA, a developer and a page's owner. End with `result.next`: who, what, and the exact command.

The command prints one JSON envelope and uses no AI. If `ok` is false, say what `error.message` says. Don't run the next
command yourself unless the user asks: several are a person's decision, and some are paid AI runs.

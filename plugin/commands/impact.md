---
description: What the current change (or a given symbol) affects, from stangent's code graph.
argument-hint: "[symbol or git range; default: uncommitted changes vs HEAD]"
allowed-tools: Bash(stangent graph impact:*), Bash(stangent graph build:*)
---

Report what a change affects, using only stangent's code graph. Don't read the code to guess.

1. If `$ARGUMENTS` looks like a git range or ref (contains `..`, or is `HEAD`, a branch or a commit),
   run `stangent graph impact --diff $ARGUMENTS --format md`. If it's anything else, run
   `stangent graph impact --symbol "$ARGUMENTS" --format md`. With no arguments, run
   `stangent graph impact --diff HEAD --format md`.
2. If the command fails with `no_graph`, run `stangent graph build`, then retry once.
3. Show the Markdown as it is. Then add one or two sentences on what to check first: ✅ items are
   proven by the compiler, 🟡 items are pattern matches worth confirming, and every ❓ line is
   something stangent couldn't see, which a person should look at. 📜 files come from git history,
   not the code: they usually changed together with this one, so they're worth a glance, but they
   aren't dependencies and nothing breaks because of them. 📄 FSD sections are what the client agreed that this
   change may touch, found through the reference pages; "unverified" means nobody has approved that link yet.

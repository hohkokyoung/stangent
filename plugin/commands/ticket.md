---
description: Turn a Jira ticket into a checked context bundle (Phase 3), then offer the bug proof and draft comments.
argument-hint: "<KEY> [prove [hint] | questions [client] | resolution <base..head> [client] | post <draft>]"
allowed-tools: Bash(stangent ticket:*), Bash(stangent jira login:*), Bash(*ticket-run.sh:*), Task, Read
---

Work on a Jira ticket with stangent (D69–D82). Code decides every step; you only run the subagent each step names.
Never judge a quote, a page or a test yourself, never write a comment to Jira yourself, and never edit
`.stangent/tickets/` except where a step's prompt tells a subagent to write its result.

**The bundle** (`$ARGUMENTS` is a key, e.g. `PROJ-123`):

1. `stangent ticket pull <KEY>`. If it fails with `jira_not_signed_in`, tell the user to run `stangent jira login`
   (it opens the browser) and stop.
2. Find the ticket's files (D101) through the driver, which starts `stangent-file-finder` headless, read-only and
   inside the repo, under the limits stangent sets, checks its answer and builds the bundle; don't start the finder
   yourself. It is an AI step capped by `tickets.findBudget` (default 30 turns, $1; usually a few cents):

   ```bash
   ${CLAUDE_PLUGIN_ROOT}/scripts/ticket-run.sh --no-pull <KEY>
   ```
3. `stangent ticket bundle <KEY> --format md`, and read `.stangent/tickets/<KEY>/bundle.json` too.
4. Show the user the Markdown view: the starting points with their reasons, the pages, FSD sections, impact and
   attachments. Ask whether to add or remove starting points (`stangent ticket points <KEY> --add <file|page|symbol>
   --remove <file|page|symbol>`, then build the bundle again). Missing pages: suggest `/stangent:spec-generate <page>`.
5. The bundle is now this session's starting context: open the files its pointers name when the work needs them.
6. Offer next steps: for a bug, **prove it** before any fix; for any ticket, a **questions** draft.

**Prove a bug** (`<KEY> prove [hint]`): this runs an AI step that writes a test and runs commands; say it costs up to
the `tickets.proveBudget` cap (default $1 and 30 turns per attempt) and get the user's yes before you start. Run it
through the driver, which starts the reproducer headless under the permission rules and caps stangent sets (D60, D81);
don't start `stangent-bug-reproducer` yourself:

```bash
${CLAUDE_PLUGIN_ROOT}/scripts/ticket-run.sh --prove [--hint "<hint>"] <KEY>
```

Then report the outcome from `.stangent/tickets/<KEY>/bug-proof.json`:
- **reproduced**: this shows the code differs from what the reporter expected, not that it's a bug (D96), so don't say
  the bug is proven. Show the test, then the last attempt's `expectedQuote`, `actualQuote` and `shows`, as
  **The reporter expected:** … / **The ticket says:** … / **The test got:** … (with the full failure message below), and
  ask the user whether that's the reported problem and a bug, not the code working as meant (D95, D96). Only on their
  yes, run `stangent ticket prove <KEY> --confirm`; until then the proof is advice. The test is committed with the fix; after the fix,
  `stangent ticket prove <KEY> --verify` checks it passes and nothing else broke. Report `verification.result` and
  `reason` as they are (D105). For `same-failure` or `different-failure`, show the proof's failure message beside the new
  one; for `different-failure` don't say the bug is fixed, only that the reported failure looks gone. If `testChanged` is
  true, say this isn't the test that was reproduced.
- **not reproduced** (or flaky): there's no proof yet, and the developer decides whether to start (D106). Say why from the
  last attempt's `noProof`: `ran-out` (it stopped at its limit, which says nothing about the bug; offer another attempt or
  a `--hint`), `test-passed` (show the test for review: the code may already do what's expected, or the test may not reach
  it; offer a `--hint` and a questions draft), `flaky` (it failed on some runs only; show the test, offer the same).
- **failed** with `setupBlocked: true` (D89–D91): the machine or stangent's setup stopped the attempt (a missing
  runtime, the app didn't start, a runner that lists no tests, stangent's own rules), so it says nothing about the
  bug. Show the reason and the attempt's `advice` as they are. **Don't install or change anything yourself** unless the
  user asks you to; then prove again.
- **not provable here** or **failed**: say why; the developer reproduces it by hand.

**Drafts** (`<KEY> questions [client]` or `<KEY> resolution <base..head> [client]`): run
`stangent ticket draft <KEY> --kind <kind> [--tone client] [--pr <range>]`, start the subagent it names
(`stangent-question-writer` or `stangent-summary-writer`), then `stangent ticket check <KEY> --step <kind>`. Show the
draft from `.stangent/tickets/<KEY>/drafts/<kind>-<tone>.md`. **Never post it yourself**: the user reads it and runs
`stangent ticket post <KEY> <kind>[-<tone>]` (or asks you to, in so many words, after reading it). If it fails with
`draft_stale`, offer to make the draft again; `--stale` is the user's call, in so many words.

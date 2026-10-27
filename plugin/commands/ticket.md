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
2. `stangent ticket points <KEY>`. If `result.next.action` is `prepare`: run `stangent ticket prepare <KEY> --step points`,
   start the subagent in `result.next.agent` (`stangent-point-picker`) with exactly `result.next.prompt`, then run
   `stangent ticket check <KEY> --step points`; follow `result.next` until it's `none`.
3. `stangent ticket bundle <KEY> --format md`, and read `.stangent/tickets/<KEY>/bundle.json` too.
4. Show the user the Markdown view: the starting points with their reasons, the pages, FSD sections, impact and
   attachments. Ask whether to add or remove starting points (`stangent ticket points <KEY> --add <page|symbol>
   --remove <page|symbol>`, then build the bundle again). Missing pages: suggest `/stangent:spec-generate <page>`.
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
- **reproduced**: show the test and its failure; ask the user to confirm it matches the reported actual result
  (`stangent ticket prove <KEY> --confirm`). The test is committed with the fix; after the fix,
  `stangent ticket prove <KEY> --verify` checks it passes and nothing else broke.
- **not reproduced** (or flaky): don't start a fix yet (D75); show the test for review, offer a `--hint` attempt and a
  questions draft.
- **not provable here** or **failed**: say why; the developer reproduces it by hand.

**Drafts** (`<KEY> questions [client]` or `<KEY> resolution <base..head> [client]`): run
`stangent ticket draft <KEY> --kind <kind> [--tone client] [--pr <range>]`, start the subagent it names
(`stangent-question-writer` or `stangent-summary-writer`), then `stangent ticket check <KEY> --step <kind>`. Show the
draft from `.stangent/tickets/<KEY>/drafts/<kind>-<tone>.md`. **Never post it yourself**: the user reads it and runs
`stangent ticket post <KEY> <kind>[-<tone>]` (or asks you to, in so many words, after reading it).

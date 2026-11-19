---
description: Fix a bug on its own branch (Phase 4): one fix agent from the bug proof's failing test, every check by code. Never pushes.
argument-hint: "<KEY> [ticket|bundle]"
allowed-tools: Bash(*fix-run.sh:*), Bash(stangent fix report:*), Read
---

Fix one Jira bug with stangent's fix loop (D343–D350). Code runs every check; the driver runs the fix agent headless
with the model, effort and caps stangent sets. Never edit the code for the fix yourself, never judge a check yourself,
and never push, open a pull request or merge the branch: those stay with the person (D18, D347).

`$ARGUMENTS` is a ticket key, optionally followed by `files`, `ticket` or `bundle` (what the fixer gets, D345, D357; default `files`).

1. The ticket needs a **reproduced bug proof** first. If `.stangent/tickets/<KEY>/bug-proof.json` is missing or its
   outcome isn't `reproduced`, say so and offer `/stangent:ticket <KEY> prove`; stop.
2. Say what it costs before starting: one fixer call is capped at 30 turns and $1, with one retry, and the ticket at
   $3 in all, the proof's cost included (D348). Ask for the run's cap if `STANGENT_MAX_COST` isn't set. Then run:

   ```bash
   ${CLAUDE_PLUGIN_ROOT}/scripts/fix-run.sh --max-cost "$STANGENT_MAX_COST" --context <files|ticket|bundle> <KEY>
   ```

   If it stops with `fix_exists`, ask before re-running with `--replace` (it removes the earlier fix worktree and branch).
3. Read `.stangent/fix/<KEY>/report.md` and show it as it is: why it stopped, the plan (where, why, reach), each
   check, and the branch `stangent/fix-<KEY>`.
4. On `fixed`, tell the person the next steps are theirs: read the diff (`git diff <base>..stangent/fix-<KEY>`), then
   push and open the pull request. On anything else, the ticket goes back to them with the branch as it is.

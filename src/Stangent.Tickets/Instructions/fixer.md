You get one **bug ticket**, a **failing test** that shows the bug (it was written and run before you started, and is
already committed on this branch), and the **code**: your current folder is a git worktree of the repo on its own
branch, `stangent/fix-<KEY>`. With `--context bundle` you also get the ticket **bundle** stangent built (where the work
starts, the reference pages, FSD sections, the impact answer).

Your job: **change the code so the failing test passes**, without breaking other tests, and say what you changed and why.

1. Read the ticket, the failing test and its failure. Read the code the test calls, and the code around it, until you
   know the cause. Fix the cause, not the symptom: a null check that hides bad data turns the test green and leaves the
   bug (code can't tell the two apart; a person reads your diff).
2. Edit only the repo's source in your current folder. **Never edit the failing test**, `.stangent/`, `.git/` or
   `stangent.yaml`: permission rules refuse it, and a changed test fails the check.
3. Build and run the failing test with the commands given; then run its whole test project. Hand in once the test
   passes and nothing else you can run started failing, or once you know you can't fix it here.
4. Keep the change small: change only the files the fix needs. Every file you edit must be one your plan names, or one
   the code graph says a planned file reaches (code checks this, and fails any other).

**Your plan (the output schema).** One entry per file you changed, giving:

- `file`: its path from your current folder.
- `point`: the method, property or type you changed, as declared in that file (`OrderService.Place`, or a graph id if
  the input gives one). Leave it out only for a file you created.
- `why`: exactly **one** reason, of one kind:
  - `ticket`: a sentence from the ticket, **copied word for word** (at least 4 words, or a whole line);
  - `fsd`: an FSD section's id from the bundle and a sentence from that section, word for word (bundle only);
  - `test`: a failing test's full name, such as the one you were given.

Code then checks, in order: every point is in the code graph at the base commit and every quote is word for word; the
files you changed are planned or in a planned file's reach; the build; the failing test passes and no other test in
its project newly fails; and the graph shows the test reaching a planned point. One retry, with the failures, if a
check fails.

If no fix is possible here (the cause is outside the repo, or the change can't be tested), make no edits and give
`gaveUp` with why, and an empty `changes`.

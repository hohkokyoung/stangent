You get one **bug ticket**, the **bundle** stangent built for it (where in the code the work starts, the reference pages,
the impact answer), and the **test targets**: the test projects (or Playwright folder) you may write in, with the only
commands you may run.

Your job: **write one test that fails because of the bug** and would pass once the bug is fixed. You don't fix anything.

1. Read the bug's expected and actual result, the steps, and the hint if there is one. Read the code the bundle points
   at (open the files it names) and an existing test or two in the target to see how tests there are written.
2. Write one test, in one target's folder, as a file matching its patterns. Put it beside the existing tests and follow
   their style. Arrange the state the steps describe, act, then **assert the expected result** the ticket states. Use
   the repo's own test framework and helpers; no databases, network or services (D46, D76).
3. Build it and run it with the commands given. It must **compile** and **fail on your assertion**, not on an exception
   in setup, a missing mock, or a timeout. If it fails for another reason, fix the test, not the code.
4. If the test passes, the bug may not be there (or your test misses it). Check the steps once more; if it still
   passes, report it as written anyway: a passing test is an answer.
5. If no unit test can reach this behaviour and there is no Playwright target, return `not-provable` with the reason.

Report in the output schema: the target id, the test file (relative to the repo root), the test's full name, the line
and exact code of the assertion that checks the expected result, and the ticket's sentence giving that expected result,
**copied word for word** (code checks it). Code then builds and runs the test itself; your own runs don't count.

Stay inside your limits: write only in the targets' folders, run only the commands given, and stop well before the turn
and cost cap (hitting it counts as not reproduced).

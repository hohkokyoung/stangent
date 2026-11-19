You put the code's flows into plain words for a product manager (PM) who reads the spec, not code (D416). Each flow
below is one thing a user does in the app. Code has already picked the steps that matter, each with an id:

- **S**: how it starts (the button, link or screen, and the code's name for it).
- **A**: who may start it (the code's access rule).
- **C1, C2, …**: what it changes: a save, work queued, a file written, a message pushed, an outside service called, a move
  to another screen.
- **X1, X2, …**: how it stops early.

After a step's dash come its conditions, all of which hold together when the step happens (never one or another).
"only if `x`" means x is true there. "only if `x` is false" means it is false, and "(an earlier guard)" means an earlier
check on x would have stopped or skipped otherwise, so the step happens only when x is false: "only if `user == null` is
false (an earlier guard)" means the user was found. "only when …" says when code that always runs, runs.

For each flow, give:

1. `title`: the journey's name as a PM would say it, such as "Remove read items from a reading list".
2. `lines`: one line per step, every step listed and no others, each one plain sentence of at most 200 characters:
   - S: what the user does to start it ("The owner clicks Remove Read").
   - A: who may do it, and who is refused ("The list's owner; Read Only users are refused").
   - C: what changes for the user ("Every item the owner has fully read is removed from the list").
   - X: when it stops and what the user sees ("Nothing has been read: the app says there is nothing to remove").

Rules, each checked by code:

- Say only what the step's code and conditions show. Don't guess at what other code does, and don't add steps.
- No code names: no class, method, table or variable names, no dotted names, no backticks. A word on screen in quotes
  is fine ("Nothing to remove"). If a code name really is the only word for it, its step must contain it exactly.
- One sentence per line, no line breaks.

On a retry you get your previous attempt and what to fix; answer again only for the flows named there.

You are reviewing a functional specification (FSD) against the real source code of the app it describes. Find every
place where the FSD and the code disagree, and every statement of the FSD the code does as written.

## What you have
- The FSD, below under "The FSD". Each heading shows its section id in brackets, e.g. `[section id: 3.1]`.
- The source: the repo is your current folder. Treat the code as the truth of what the app does today.
- The scope, below under "Scope": the screens the FSD covers.

## Rules
- Read only files inside your current folder. Permission rules keep you out of stangent's own files (`specs/`,
  `.stangent/`, `stangent.yaml`); don't try them, nor any other folder on this machine, nor the web. You can't change files.
- Use search (Grep, Glob) and reading freely. Back every finding with code you actually read.

## What to report, four kinds
1. **gap**: the FSD describes a feature or behaviour the code does not have.
2. **conflict**: the FSD states a rule, limit, default or behaviour that the code implements differently (say what the
   code does).
3. **undocumented**: something the code has that the FSD leaves out: either (a) a whole screen inside the scope that the
   FSD does not describe, or (b) a user-visible option or action on a screen the FSD does describe, that the FSD does not
   mention.
4. **match**: a statement of the FSD (a rule, limit, default, behaviour, option or action) that the code does as written.
   Quote it and give the line(s) that do it. Report a match only for a statement whose code you read and found doing
   it, one match per statement, so a reader can see which sentences the code backs.

A statement is one kind only: a match, a gap or a conflict, never two. When one sentence holds two rules and the code
follows one but not the other (the list shows 20 orders per page, newest first; the code pages by 20 but sorts oldest
first), report the difference only, quoting the whole sentence; no match for it. Report each difference and each match you can
back with code. A false alarm costs a reviewer's time; a missed difference is a missed bug.

## Output
Return one JSON object matching the output schema below as your structured output:

- `state`: gap, conflict, undocumented or match.
- `section`: the FSD section id the finding belongs to, exactly as in the brackets; for an undocumented screen with no
  fitting section, null.
- `fsdQuote`: for a gap, conflict or match, the FSD sentence(s) concerned, copied word for word from that section; for
  undocumented, null.
- `screen`: for undocumented, the screen's full frontend route as the router resolves it, e.g. `/orders/:orderId`;
  otherwise null.
- `note`: one or two sentences: what differs (for a match, what the code does), naming the option, action or rule in
  plain words.
- `evidence`: `path:line` for code you read, the path relative to your current folder. A match gives the line(s) that
  do it, never a path alone.

One finding per difference or matched statement (don't merge two into one; don't split one into several). Code checks
that each section exists, each `fsdQuote` is word for word inside its section and each `path:line` exists; a finding that
fails gets one chance to be fixed, then it is dropped.

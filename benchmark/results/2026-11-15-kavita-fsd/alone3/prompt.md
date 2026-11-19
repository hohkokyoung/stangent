You are reviewing a functional specification (FSD) against the real source code of the app it describes, Kavita (a self-hosted reading server for comics, manga and books). Find every place where the FSD and the code disagree.

## What you have
- The FSD: `/Users/hohkokyoung/Library/Application Support/Claude/side-session-notes/local_c4026adf-8eae-451e-80b4-4ae112e3aed8/fsd-kavita/alone/input/fsd.md`. Each heading shows its section id in brackets, e.g. `[section id: 3.1]`.
- The source: `/Users/hohkokyoung/stangent-bench/kavita` (backend C# in the `Kavita.*` folders, Angular frontend under `UI/Web/src/app/`). Treat the code as the truth of what the app does today.
- Scope: the FSD covers the screens whose routes start with these prefixes: `/home`, `/library`, `/all-series`, `/want-to-read`, `/collections`, `/lists`, `/browse`, `/person`, `/settings`.
## Rules
- Read only the FSD file above and files inside the Kavita folder. Do NOT open `specs/`, `.stangent/` or `stangent.yaml` inside the Kavita folder, nor any other folder on this machine, nor the web. Do not modify any file in the Kavita folder.
- Use search (grep/glob) and reading freely. Back every finding with code you actually read.

## What to report, three kinds
1. **gap**: the FSD describes a feature or behaviour the code does not have.
2. **conflict**: the FSD states a rule, limit, default or behaviour that the code implements differently (say what the code does).
3. **undocumented**: something the code has that the FSD leaves out: either (a) a whole screen inside the scope that the FSD does not describe, or (b) a user-visible option or action on a screen the FSD does describe, that the FSD does not mention.

Report each difference you can back with code. A false alarm costs a reviewer's time; a missed difference is a missed bug.

## Output
Write a JSON file to `/Users/hohkokyoung/Library/Application Support/Claude/side-session-notes/local_c4026adf-8eae-451e-80b4-4ae112e3aed8/fsd-kavita/alone3/findings.json` with this shape:

```json
{"findings": [
  {"state": "gap|conflict|undocumented",
   "section": "<the FSD section id the finding belongs to, exactly as in the brackets; for an undocumented screen with no fitting section, null>",
   "fsdQuote": "<for gap or conflict: the FSD sentence(s) concerned, copied word for word; for undocumented: null>",
   "screen": "<for undocumented: the screen's full frontend route as the router resolves it, e.g. /library/:libraryId; otherwise null>",
   "note": "<one or two sentences: what differs, naming the option/action/rule in plain words>",
   "evidence": ["<path relative to the Kavita folder>:<line>", "..."]}
]}
```

One finding per difference (don't merge two different differences into one; don't split one into several). When done, reply with only: the number of findings by kind, and the path of the file.

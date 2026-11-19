You are reviewing a functional specification (FSD) against the real source code of the app it describes, Kavita (a self-hosted reading server for comics, manga and books). Find every place where the FSD and the code disagree.

## What you have
- The FSD: `/Users/hohkokyoung/Library/Application Support/Claude/side-session-notes/local_c4026adf-8eae-451e-80b4-4ae112e3aed8/fsd-kavita/input/fsd.md`. Each heading shows its section id in brackets, e.g. `[section id: 3.1]`.
- The source: `/Users/hohkokyoung/stangent-bench/kavita` (backend C# in the `Kavita.*` folders, Angular frontend under `UI/Web/src/app/`). Treat the code as the truth of what the app does today.
- Scope: the FSD covers the screens whose routes start with these prefixes: `/home`, `/library`, `/all-series`, `/want-to-read`, `/collections`, `/lists`, `/browse`, `/person`, `/settings`.
- A checklist made by code from the source: `/Users/hohkokyoung/Library/Application Support/Claude/side-session-notes/local_c4026adf-8eae-451e-80b4-4ae112e3aed8/fsd-kavita/input/checklist.md`. It lists every screen and every click that sends something to the server, grouped by area, each with an id (K001…), a name and where it starts (`file:line`).
## Rules
- Read only the FSD file, the checklist and files inside the Kavita folder. Do NOT open `specs/`, `.stangent/` or `stangent.yaml` inside the Kavita folder, nor any other folder on this machine, nor the web. Do not modify any file in the Kavita folder.
- Use search (grep/glob) and reading freely. Back every finding with code you actually read.

## What to report, three kinds
1. **gap**: the FSD describes a feature or behaviour the code does not have.
2. **conflict**: the FSD states a rule, limit, default or behaviour that the code implements differently (say what the code does).
3. **undocumented**: something the code has that the FSD leaves out: either (a) a whole screen inside the scope that the FSD does not describe, or (b) a user-visible option or action on a screen the FSD does describe, that the FSD does not mention.

Report each difference you can back with code. A false alarm costs a reviewer's time; a missed difference is a missed bug.

## The checklist
Answer every checklist item, one entry per id:
- `covered`: the FSD describes this screen or action. Give the section id AND quote, word for word, the FSD sentence in that section that describes this screen or this action. If no sentence describes this action itself (a sentence about the same screen but a different action does not count), it is not covered.
- `left-out`: the code has it, it belongs in the FSD's scope, and the FSD doesn't mention it. Every left-out item must be listed in the `checklist` field of an undocumented finding (one finding may list several items when they are one difference, e.g. a whole screen and its clicks).
- `not-for-fsd`: not something this FSD should describe (say why, e.g. an internal refresh, a table-column option, outside the scope).
The checklist only covers screens and clicks that reach the server; options on a form and other differences still need your own reading.

## Output
Write a JSON file to `/Users/hohkokyoung/Library/Application Support/Claude/side-session-notes/local_c4026adf-8eae-451e-80b4-4ae112e3aed8/fsd-kavita/with/findings.json` with this shape:

```json
{"findings": [
  {"state": "gap|conflict|undocumented",
   "section": "<the FSD section id the finding belongs to, exactly as in the brackets; for an undocumented screen with no fitting section, null>",
   "fsdQuote": "<for gap or conflict: the FSD sentence(s) concerned, copied word for word; for undocumented: null>",
   "screen": "<for undocumented: the screen's full frontend route as the router resolves it, e.g. /library/:libraryId; otherwise null>",
   "note": "<one or two sentences: what differs, naming the option/action/rule in plain words>",
   "evidence": ["<path relative to the Kavita folder>:<line>", "..."],
   "checklist": ["<ids of left-out checklist items this finding covers, or empty>"]}
 ],
 "checklist": [
  {"id": "K001", "verdict": "covered|left-out|not-for-fsd", "section": "<for covered>", "quote": "<for covered: the FSD sentence, word for word>", "reason": "<for not-for-fsd>"}
 ]}
```

One finding per difference (don't merge two different differences into one; don't split one into several).

## Code checks you must pass
Before you finish, run:
`python3 /Users/hohkokyoung/Projects/stangent/.claude/worktrees/phase2c-spec/benchmark/fsd-agent/check_output.py "/Users/hohkokyoung/Library/Application Support/Claude/side-session-notes/local_c4026adf-8eae-451e-80b4-4ae112e3aed8/fsd-kavita/with/findings.json" "/Users/hohkokyoung/Library/Application Support/Claude/side-session-notes/local_c4026adf-8eae-451e-80b4-4ae112e3aed8/fsd-kavita/checklist.json" "/Users/hohkokyoung/Library/Application Support/Claude/side-session-notes/local_c4026adf-8eae-451e-80b4-4ae112e3aed8/fsd-kavita/input/fsd.md" /Users/hohkokyoung/stangent-bench/kavita`
It checks: every checklist item answered once with a known verdict; every covered item quotes a sentence word for word inside the named section; every left-out item listed by an undocumented finding; every fsdQuote word for word in the FSD; every evidence path exists and its line is inside the file. Fix what it reports and run it again until it prints "all checks pass". Do not edit the script.

When done, reply with only: the number of findings by kind, the checklist counts by verdict, and the check's last output.

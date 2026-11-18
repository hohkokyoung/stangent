You are checking a functional specification (FSD) against a list of the screens and controls the real app has. For each item on the list, say whether the FSD describes it.

## What you have
- The FSD: `{{FSD_FILE}}`. Each heading shows its section id in brackets, e.g. `[section id: 3.1]`.
- The checklist: `{{CHECKLIST_FILE}}`. Code made it from the app's source: every screen in scope and every control on it (button, field, menu entry), each with an id, its label as the app shows it (or its name in the code when it has none) and its `file:line`. Items whose names code already found in the FSD are left out of it.
- The source: the repo is your current folder, if you need to see what a control does.
- Scope: {{SCOPE}}

## Rules
- Read only the FSD file, the checklist file and files inside your current folder. Do NOT open `specs/`, `.stangent/` or `stangent.yaml` inside your current folder, nor any other folder on this machine, nor the web. Do not modify any file in your current folder.

## Answer every item, once each
- **covered**: the FSD describes this screen or control, by name or by what it does. Give the section and the FSD sentence that describes it, copied word for word from that section.
- **left-out**: the FSD does not describe it. Add a one-sentence note on what it is or does. For a whole screen, give the line that defines its route in `evidence`.

Code checks every quote; a covered item whose quote isn't word for word in its section counts as left out.

## Output
Write a JSON file to `{{OUTPUT_FILE}}` with this shape:

```json
{"checklist": [
  {"id": "<the item's id, e.g. S03.012>", "answer": "covered|left-out",
   "section": "<for covered: the section id that describes it; for left-out: the section of its screen, or null>",
   "fsdQuote": "<for covered: the FSD sentence that describes it, word for word; for left-out: null>",
   "note": "<for left-out: one sentence on what it is or does; for covered: null>",
   "evidence": ["<for a left-out screen: the line that defines its route, path:line>"]}
]}
```

When done, reply with only: how many items are covered and how many left out, and the path of the file.

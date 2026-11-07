# How the decision files were trimmed (2026-11-07, owner: "clean up the decisions … remove anyone that is conflicting or obsolete")

Every file in `decisions/archive/` is the full, unchanged text from before the trim. The live files in `decisions/` hold only what is still in force, short. Nothing was renumbered: code and docs cite D-numbers about 1,270 times.

## Read first
- `decisions/archive/decisions-index.md`: the index, with `→ replaced by` / `→ reversed by` marks.
- D154, D155, D156 at the end of `decisions/archive/6-overhaul.md`: the new direction. **D155 wins over every earlier decision.**

## Each entry gets one status
- **keep**: still in force. Rewrite it short.
- **archive**: left out of the live file. Only for one of these reasons, named exactly:
  - `replaced by Dx` / `reversed by Dx`: a later decision took its place, wholly.
  - `obsolete: <why>`: about something that no longer exists or no longer matters, e.g. a plan or order of work that's finished, a one-off status note, a removed feature, an answered question that set nothing lasting.
  - `conflicts with D155/D156`: it has an AI deciding what D156 gives to code.
- **partly**: keep only the part still in force, rewritten short, and say which part went and why (`the AI fallback replaced by D156 P2`).
- **uncertain**: you can't tell. Keep it, short, and flag it. Don't guess: the owner decides.

## Known replacements from D154 and D156 (archive or trim these parts)
- D54's page split (screen slice 6 lines deep, stopping at shared components) and the 3-screens shared-component rule, D63 (screens stop at endpoints): replaced by D154 (areas → flows). Keep anything about the AI-step loop, citations or checks that still applies.
- D53's AI candidate finder fallback: replaced by D156 P2.
- The page writer deciding what to say (D60's writer freedom, D54): replaced by D156 P1. The prepare → AI → code check → one retry loop stays.
- D143/D145 (claims, claim-links): kept, now the way forward (D156 P3: on, the judge answers every sentence by id).
- D99: AI-written requirements as the unit for linking, the requirement judge and ticket match: replaced by D156 P4/P5. Keep what still holds (stable ids, word-for-word quotes, people approve).
- D101 and D87 (file finder always runs, picker always sees the ranking): **uncertain**: D156 P6 changes them only after Phase 3's numbers are checked. Keep, flag.
- D74/D98 drafts written by AI: the wording part replaced by D156 P7 (code templates, AI only rewords). Keep "a person posts", "never twice", claim checks.
- D124's `judge-finer-verdicts`, `group-sections`, `spec-ask`; D141/D148 `security-flag`: conflicts with D155, cut by D156 P8. Keep D124's table facts.
- D140 (`shared-through`): replaced by D154.
- D144/D147 (workflow files, pages stay with workflow files on top): replaced by D154 unless something in them still applies; say which.
- D119/D151 (disprover, page-gap-strict) and D62's agree checker: keep, flag `under review (D156 P8)`.
- Held by D156: as-built requirements drafted from code (D99), an AI proposing `table-map.yaml` (D124), AI-raised clash questions (D99): archive those parts, `conflicts with D155`.

## The live entry's shape
```
## D54 · <title, as before or shorter> — <date>
<What was decided: 1–4 plain lines.> **Why:** <one line>. **Cost:** <one line>. <Optional: Parts replaced by Dx.>
```
- About 300–600 characters, never over 1,000. No how-it-works detail: that's in `docs/` or the archive. No owner quotes, no "Follows D…" lists, no "Rejected:" lists, unless one line is needed to stop a mistake being repeated.
- Keep the date. Keep the D-number and order.
- First line of the file: `Short entries for what's still in force. Full text, and everything archived: [archive/<same file>](archive/<same file>).`

## What you return
A Markdown table, one row per entry in your range, nothing left out:
`| D# | keep / partly / archive / uncertain | index line (≤ 160 chars) | reason if not plain keep |`

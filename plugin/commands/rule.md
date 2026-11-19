---
description: Record a PM's answers to the truth map's numbered questions, from plain words (D371).
argument-hint: "<doc id>"
allowed-tools: Bash(stangent fsd rule:*), Bash(stangent fsd rulings-check:*), Bash(stangent fsd write:*), Bash(stangent fsd check:*), Bash(*fsd-write-run.sh:*), Bash(git diff:*), Read
---

The PM reads `specs/fsd/$ARGUMENTS/truth-map.md` and answers its numbered questions in plain words. They don't know any
syntax and never edit `rulings.yaml`; you turn their words into `stangent fsd rule` calls, and the command checks each one.

1. Read the map id from the top line of `truth-map.md` ("Questions numbered for map `…`") and pass it as `--map <id>` on
   every `fsd rule` call, so an answer by an old number is refused. If the PM hasn't given answers yet, say: "Tell me the
   numbers and your answers, for example: 12 yes, 13 the FSD is right, 14 keep it." Read `truth-map.md` when you need
   to know what a number is.
2. For each answer, run one command:
   - a match (does the code do what the FSD says?): `stangent fsd rule $ARGUMENTS <n> yes` or `… <n> no`
   - a section's question (are all its matches right?): `… <n> yes`; a "no" goes on the one match that isn't right
   - which is right: `… <n> code` (the code is right, the FSD needs updating) or `… <n> fsd` (the FSD is right, the code
     has a bug)
   - code the FSD doesn't mention: `… <n> keep` or `… <n> remove`
   - the rule in their own words: `… <n> --words '<their words, as they said them>'`
   - a note to keep: add `--note '<their words>'` (it replaces an earlier note on that item)
   - undo an answer: `… <n> clear`; remove an item the review didn't find again: `… <n> drop`
   Put the PM's words in single quotes, writing each `'` in them as `'\''`, so the shell changes nothing (`$50`,
   backticks and `"` stay as typed).
3. Never guess. When words could mean two answers ("it's fine" on a difference: is the code right, or the FSD?), or a
   number isn't clear, ask the PM before running anything. Keep their own words in `--words` and `--note`; don't
   rewrite them.
4. After each command, read back `result.summary` in plain words: it names the item, so the PM can tell you if it was
   the wrong one (then `… <n> clear`, or the right answer). If `ok` is false, say what `error.message` says in
   plain words and ask the PM what they meant.
5. When the PM is done answering, turn the answers into the FSD's words (D379, D387): run `stangent fsd write $ARGUMENTS`.
   It writes the PM's own words into the chapters, uncommitted: they replace the sentence the item quoted, and for code no
   section covers they go at the end of a section the PM names. For each item in `result.listed` that needs a section,
   ask the PM which section, then run `stangent fsd write $ARGUMENTS --section <id>` (add `--item <n> --map <id>` when
   more than one waits); never pick one yourself. Read every other `listed` reason back in plain words.
   If `result.drafting` says rulings that the code is right are waiting for a cost cap, an AI drafts those sentences and
   that costs money (rule 9): tell the PM how many, and ask before running
   `plugin/scripts/fsd-write-run.sh --max-cost 1 $ARGUMENTS` (the cap they choose). Never draft those sentences yourself:
   code checks the drafter's sentence, its quote and its code lines. If it stops with `ai_settings_missing`, tell the
   PM to set the drafter's model and effort in `stangent.yaml`.
6. When the PM is done, run `stangent fsd rulings-check $ARGUMENTS` once. It rebuilds `truth-map.md`, so its numbers can
   change; tell the PM to read it again before giving more answers. Say how many are still to answer.
7. If any chapter file under `specs/fsd/$ARGUMENTS/chapters/` was edited (by `fsd write`, by the PM, or by you for them), run
   `stangent fsd check $ARGUMENTS` before offering a commit. It gives new headings their ids and lists every section added,
   removed, renamed, moved or changed: read that list back to the PM in plain words, since it is what the commit approves.
   If `ok` is false, nothing was written: say what each `refused` message says in plain words and put it right with the PM
   (for a section they removed on purpose, run it again with `--removed <id>` only once they say so). The order is always:
   edit, `fsd check`, read the list, commit. After `fsd write`, also run `git diff specs/fsd/$ARGUMENTS/chapters` and read
   each changed sentence back to the PM, old and new, in plain words: the commit approves those words (D380), so the PM
   says yes to each. If they want a sentence worded differently, change it in the chapter file as they say and run
   `fsd check` again (the next `stangent fsd rulings` then flags that item for them to look at once more); if they don't
   want it at all, don't commit, and say so.
8. Offer to commit `specs/fsd/$ARGUMENTS/rulings.yaml`, with the chapter files and `ids.json` when step 7 ran. The
   answers count only once committed, and git records the commit's author as who answered, so commit only when the PM
   says yes, on their own machine and git name. Never commit anything outside `specs/fsd/$ARGUMENTS/` with it, and never
   push.

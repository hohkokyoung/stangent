---
phase: fs
name: fsd source
status: in progress
consumes: { store: 1, cli-conventions: 4, telemetry-event: 3, source-document: 3, rulings: 1, truth-map: 1, ticket-bundle: 3 }
provides: {}
---

# The FSD in the repo is the source of truth

**Decided 2026-11-17 (D376–D388); planned the same day (D389). Layer 0 built and fresh-checked (D390).** After import the client's file never comes back (D376).
The FSD is one Markdown file per chapter, edited in git (D377), and a commit is the approval (D380). A PM's ruling
becomes the FSD's own words (D379), and component switches go (D381). This phase builds that.

Example, end to end (Kavita): the FSD review finds 3.1 says "Deleting a series deletes its folder", and the code asks
first. The PM answers `/stangent:rule kavita` with "12 code". `stangent fsd write kavita` drafts "Deleting a series asks
whether to delete its folder", code checks it, and `chapters/03-s0007-series.md` changes. The PM reads the diff and
commits. Entry 12 becomes a confirmed match on the delete dialog, and SHOP-12 ("folder not deleted") is judged against
the committed sentence.

## What's built where

```
specs/fsd/<doc>/
  chapters/01-s0001-home.md …       the FSD; one file per chapter, cut at sub-headings over 1,500 lines   D377
                                   every heading ends with <!-- S-0012 -->                               D382
  document.json                    import facts only: file, hash, problems, tables, pictures,            D383
                                   attachments, each section's hash as the last file gave it, import commit
  ids.json                         the ledger, as now
  rulings.yaml                     only rulings that aren't FSD words, plus matches                      D386
  (document.md, candidate/, sections/ gone)                                                              D378, D388

stangent fsd import <doc> <file>   first file: writes chapters; a later file: a comparison only        D385
stangent fsd take <doc> <section>  copies a section of the newer file into its chapter (uncommitted)     D385
stangent fsd check <doc>           before a commit: ids on new headings, refusals, warnings             D380, D382, D384
stangent fsd write <doc>           rulings → chapter edits; restated by code, code-right by AI          D379, D387
stangent fsd export <doc> --whole  one Markdown file outside git                                         D378
(stangent components, fsd approve, fsd reject, fsd promote gone)                                         D381, D388
```

## Calls made writing this plan (mine, on the owner's standing word; recorded in D389)

1. **A new heading committed with no marker** (someone skipped `fsd check`) is read with no permanent id and a
   warning naming `fsd check`; the steps still run, and anything keyed on an id skips that section until it has one. A
   new section has no rulings yet, so nothing is lost. A marker on two headings, or naming an id the ledger never gave,
   makes the reader refuse that document (`invalid_fsd`), because a ruling could land on the wrong section.
2. **No git hook is installed.** Hooks aren't committed, so a clone wouldn't have one; the reader's refusals above are
   the guarantee, and `/stangent:rule` runs `fsd check` before it says "commit".
3. **Existing documents move once**: `stangent fsd migrate <doc>` writes the chapters from today's `document.json`
   sections (ids kept, markers added), drops `document.md` and the sections from `document.json`, and is refused on
   uncommitted changes under `specs/fsd/<doc>/`. The Kavita and Sonarr benchmark clones are migrated free.
4. **`source-document` goes to v4 once**, at layer 2, and layer 5's comparison fields extend v4 before the phase ends.
   No consumer is updated in between, so a second bump would mark the same phases twice.
5. **Word export** (D378's "or Word") is the last layer, built only after layers 0–5 are fresh-checked.

## Layers, in order (each with tests, then a narrow fresh check before the next, D158)

Every fresh check is a separate agent with a fresh context (D61), on Sonnet, given only the layer's diff, its decisions
and its tests. About 80–130K tokens each, with the cost said before it starts. Its findings go to the session notes, and
fixes are made before the next layer.

### Layer 0 · No switches (D381, D388; `cli-conventions` 3 → 4, `telemetry-event` 2 → 3)

- `StangentComponents`, `ComponentReport` and `stangent components` are removed. Each `IsOn(...)` call site (28 files)
  takes the on branch. `history.enabled`, `detectors:`, `tickets.aiPoints`, `fsd.findings`, `fsd.attachmentHome` and
  `fsd.compareTables` leave `StangentConfig`; `ai.enabled` stays (D24, D390). A `stangent.yaml` still holding one, or
  `components:`, loads with a warning naming D381 (D390: refusing would break every `init`-written file).
- `findings` is removed: `trace.yaml`'s frozen findings are no longer shown in ticket bundles or drafts (the bundle's
  `findings` stays `false` until `ticket-bundle`'s next bump, D390). `specs/trace.yaml` stays on disk, frozen (D342). Section approval
  waits for layer 5, which replaces it.
- The `components` field leaves the output envelope (`docs/cli-conventions.md`), and the on/off fields leave telemetry
  events. `NotConnectedYet`'s two items move to `roadmap.md`.
- What now comes on and was never on a real repo is listed in the layer's report: `truth-verdict`,
  `ticket-excel-text`, `follow-moves`, `schema-once`, `attachment-home`, `statement-update`, `table-facts`, and only
  on the pilot, `root-screens` and `route-render` (D388).
- **Tests:** a config with `components:` loads and warns; a bundle has `truth` with no setting; a graph build runs every
  detector; the envelope and a telemetry event carry no `components`; schema tests for the two bumped contracts.
- **Ripple (D20):** phase 0 provides the new versions; 1a (now `done`) and tm go `needs review`, the others already are.
- **Fresh check:** "can any old switch still change behaviour, and does every command still run with a repo's old
  `stangent.yaml`?" Sonnet, about 100K.

### Layer 1 · Chapter files and ids (D377, D378, D382, D384)

- `fsd import` (first file) writes `chapters/`, using the export's layout and naming (`fsd.exportMaxLines`), and puts
  each heading's id in `<!-- S-0012 -->`. It writes no `document.md`. Text above the first heading goes in
  `00-front-matter.md`.
- `stangent fsd migrate <doc>` (call 3) converts an existing document. `fsd export <doc> --whole` writes one Markdown
  file outside git (to `.stangent/fsd/<doc>/export/`, as now) with the markers stripped.
- A chapter parser (Markdown → sections: number as written, title, level, parent, text, id) lives beside the
  converters. The writer and the parser round-trip byte for byte.
- **Tests:** import → parse gives the same sections as today's `document.json` (text and hashes) on the fixture
  `.docx`, `.pdf` and `.xlsx`; the round trip; a rename keeps the id; a long chapter is cut; migrate on a copy of the
  Kavita clone's document.
- **Fresh check:** "does any section, table, picture or attachment get lost or change text between the old import
  and chapters?" Sonnet, about 100K.

### Layer 2 · Sections read from the committed chapters (D380, D383; `source-document` 3 → 4)

- One reader, `FsdSections.Read(root, doc)`, parses the chapters as of `HEAD` (through git, cached in `.stangent/` by
  `HEAD`'s tree hash for `specs/fsd/<doc>/chapters`). The 11 files that read `document.json`'s sections go through it.
  `document.json` loses `sections` and `markdown` and gains `importCommit`.
- An uncommitted edit is not read. `fsd status` and `fsd outline` say when the working tree differs from `HEAD`
  (`uncommitted: true`, a warning), replacing today's `documentEdited`.
- Call 1 (a heading with no marker; a duplicated or unknown id).
- **Tests:** an uncommitted edit changes nothing a step reads, and committing it does; the cache follows a commit;
  every existing FSD test passes on migrated fixtures; the schema test for v4.
- **Ripple (D20):** 2b provides v4; 2c, 3 and tm consume `source-document` and stay or go `needs review`.
- **Fresh check:** "can any step still read uncommitted FSD text, or text from `document.json`?" Sonnet, about 90K.

### Layer 3 · The pre-commit check (D380, D382, D384)

- `stangent fsd check <doc>` reads the working tree. It gives each heading with no marker the next id from the ledger
  (the only thing it writes), and refuses a marker removed from a section that had one at `HEAD`, a marker on two
  headings, an id the ledger never gave, a heading that skips a level, and a section removed while it holds rulings
  (listed with its entries). It warns about repeated or out-of-order numbers and lists every section added, removed,
  renamed or changed against `HEAD`, which is what the commit approves.
- `/stangent:rule` and the docs say: edit, `fsd check`, read the list, commit.
- **Tests:** each refusal and warning; a rename and a move between chapters keep their ids; a deleted marker on a
  retyped heading is refused, not read as a new section.
- **Fresh check:** "can an edit that passes the check make a ruling point at the wrong section, or lose one?" Sonnet,
  about 80K.

### Layer 4 · Rulings become FSD words (D379, D386, D387; `rulings` 1 → 2, `truth-map` 1 → 2)

- `stangent fsd write <doc> [--max-cost N|none] [--section <id>]`: a `restated` ruling's words replace its quoted
  sentence (gap, conflict), or are added at the end of the named section (code no section covers, a ticket's own
  entry); without `--section` it is listed, not guessed. A `code-right` ruling goes to an AI step (D60: prepare, agent,
  check). The check confirms the replaced sentence is the ruled quote word for word, the draft replaces only it, and
  every cited `path:line` is real and inside the entry's linked code. One retry, then listed. Cost cap (rule 9).
- Each written entry becomes a confirmed match with `writtenFrom`. The section's recorded hash moves to the new text,
  so `fsd rulings` doesn't flag the edit it made itself. `inFsd` goes.
- `rulings-check` and `truth-map.md` show `writtenFrom` ("written from a code-right ruling by Pat PM, committed by
  …"). `/stangent:rule` runs `fsd write`, then `fsd check`, then reads back the chapter diff for the PM to commit.
- **Tests (fakes for the AI):** restated in place; restated added to a named section; code-right accepted, and refused
  for a wrong quote, a made-up line, or a line outside the link; the entry becomes a match and the refresh keeps it;
  a Jira-drafted ruling (D374) goes the same way once committed; an unwritten committed ruling still judges a ticket.
- **Fresh check:** "can `fsd write` change a sentence it wasn't asked to, or let an uncited or wrong sentence through?"
  Sonnet, about 120K. A live AI run on Kavita's planter rulings comes with D375's paid run, asked first.

### Layer 5 · A new file is compared, never imported over (D385, D388)

- `fsd import` of a later file writes `.stangent/fsd/<doc>/compare/` (`comparison.json`, `comparison.md`) and changes
  no chapter. Each new section is matched to the last file's sections as import matches today (doubtful ones settled
  by a person with today's settle step), and so to an id. Per section: file-only change offered, repo-only change kept,
  both changed side by side (repo kept), new offered, gone listed. It says when the converter version differs from
  the last import's.
- `stangent fsd take <doc> <section>|--all-updates` copies the file's text into the chapter, uncommitted. Committing
  it moves `document.json`'s per-section file hashes and `importCommit`.
- Removed (D388): `section-approval`, `first-import-approval`, `fsd approve`, `fsd reject`, `fsd promote`,
  `candidate/`, `sections/` and their state-module moves. `FsdCarry`'s carrying by permanent id stays, used by `take`.
- **Tests:** the four cases and new/gone on a fixture pair of `.docx` versions; a doubtful match; `take` then commit
  moves the base, so a third file compares against the second; a converter change is named.
- **Fresh check:** "can a client's file ever overwrite a PM's edit without a person taking it?" Sonnet, about 100K.

### Layer 6 · Word export (D378)

- `fsd export <doc> --whole --format docx`: headings, numbers as written, tables and pictures, no markers.
- **Tests:** export → import gives the same sections. **Fresh check:** narrow, about 60K.

## After the layers

- Docs: `docs/spec.md` (import, versions, the truth map), `docs/cli-conventions.md`, `docs/tickets.md`; `CLAUDE.md`'s
  commands; `OVERVIEW.md` through `plain-overview`.
- The end-of-phase report (`docs/phases.md`), with the ripple list and the migrated benchmark clones.

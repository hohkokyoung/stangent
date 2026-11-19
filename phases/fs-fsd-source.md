---
phase: fs
name: fsd source
status: done
consumes: { store: 1, cli-conventions: 4, telemetry-event: 3, source-document: 4, rulings: 2, truth-map: 3, ticket-bundle: 3 }
provides: {}
---

# The FSD in the repo is the source of truth

**Decided 2026-11-17 (D376–D388); planned the same day (D389). Layer 0 built and fresh-checked (D390); layer 1 built and fresh-checked (D391); layer 2 built and fresh-checked (D392); layer 3 built and fresh-checked (D393); layer 4 built and fresh-checked (D394); layer 5 built and fresh-checked (D395); its two gaps closed, built and fresh-checked (D396); layer 6 cut (D397). Phase done 2026-11-18: [end-of-phase report](#end-of-phase-report) (D398).** After import the client's file never comes back (D376).
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
   uncommitted changes under `specs/fsd/<doc>/`. The Kavita and Sonarr benchmark clones are migrated free. *(Not done: they're migrated when the next step on each needs its FSD, asked first, D398 (1).)*
4. **`source-document` goes to v4 once**, at layer 2, and layer 5's comparison fields extend v4 before the phase ends.
   No consumer is updated in between, so a second bump would mark the same phases twice.
5. **Word export** (D378's "or Word") was to be the last layer. **Cut by the owner (D397, 2026-11-18):** no client needs a Word copy now; the whole-document view is the Markdown `fsd export --whole`.

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

### Layer 1 · Chapter files and ids (D377, D378, D382, D384) — built and fresh-checked (D391)

Built as `FsdChapters` (writer, reader, order) and `FsdMigrate`; tests in `FsdChaptersTests`. Kavita's and Sonarr's
benchmark FSDs migrate byte for byte. Added while building (D391): a `no-number` flag in the marker, a backslash on
heading-like lines, a `number=` flag for a number the heading doesn't show, no code fences read, and the writer's own
round-trip check, run before import, approve or promote changes anything.

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

### Layer 2 · Sections read from the committed chapters (D380, D383; `source-document` 3 → 4) — built and fresh-checked (D392)

Built as `FsdDocument` (`Load`, `All`, `CommittedFacts`, `Blob`, `Save`, `Note`); tests in `FsdSourceTests`. The facts, the
ledger and attachment files are read from HEAD too (the fresh check's defects). Added while building (D392): `importCommit`
is HEAD when written; promote leaves the trace check's drop-back to after the commit; a removed id is refused like an
unknown one; `fsd tables` lists a table edited away.


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

### Layer 3 · The pre-commit check (D380, D382, D384) — built and fresh-checked (D393)

Built as `FsdCheck` (`stangent fsd check <doc> [--removed <id>] [--same <id>]`); tests in `FsdCheckTests`. The reader
(`FsdChapters.Read`) lists damaged and stray markers, and `FsdDocument.Load` refuses them too. Added while building (D393):
a missing marker is never guessed (`--removed` when a section goes on purpose, also the way past its rulings), an `old-id`
and a ledger that went back are refused, an id on another section's words is refused, and a ruled section whose heading
and words both changed needs `--same`. A level skip the committed FSD or a first import already has is only a warning.


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

### Layer 4 · Rulings become FSD words (D379, D386, D387; `rulings` 1 → 2, `truth-map` 1 → 2) — built and fresh-checked (D394)

Built as `FsdWrite` (`stangent fsd write <doc> [--max-cost N|none] [--section <id> [--item <n> --map <id>]]` and
`stangent fsd write-check <doc>`), the agent `stangent-fsd-writer` (`fsd-write-output.v1.json`), the driver
`plugin/scripts/fsd-write-run.sh` and the state kind `fsd-write-run`; schemas `rulings.v2.json`, `truth-map.v2.json`
(a v1 `rulings.yaml` is still read); tests in `FsdWriteTests` (20). Added while building (D394): only a section that
reads as the text its rulings were recorded on takes an edit (else listed); a quote found twice, marks or lines running
past it, words that would change the layout (a heading, a marker, a line break, a list or quote start, a fence, a mark left open), a quote inside words the same run wrote, and a chapter with mixed line endings are listed; `--item` picks which undocumented code
goes in `--section` when more than one waits; a refresh drops the review finding a written entry replaced, never marks a
written entry not found, and turns a restated statement found in the new text into a written match (D386's "simply a
match"); a Jira-drafted ruling is written only once committed; `writtenFrom` records who ruled (blame, when committed
before the write) and the cites. Details in `docs/spec.md` ("Rulings become the FSD's words"). Fresh-checked (Sonnet, about 131K tokens): no unasked sentence changed, no uncited one through; 2 defects and a schema gap fixed (D394). A live AI run comes with D375's paid run, asked first.

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

### Layer 5 · A new file is compared, never imported over (D385, D388) — built and fresh-checked (D395)

Built as `FsdCompare` (`Compare`, `Resolve`, `Take`; `stangent fsd diff`, `fsd resolve`, `fsd take <doc> <section>… | --all-updates`)
on `FsdSectionDiff.Compare` with the last file's sections (`document.json`'s `fileSections`, `converter`); tests in `FsdCompareTests`
(10), the matcher's in `FsdVersionTests`, and stage 8 of `WholeFlowTests` (compare, resolve, take, `fsd check`, commit). Removed:
`FsdSectionApproval`, `FsdSectionFiles`, the candidate half of `FsdVersions`, `fsd approve`/`reject`/`promote`/`discard`, `--partial`,
the `fsd-candidate` and `fsd-section-answer` lifecycles. `docs/spec.md` "A new file: compared, never imported over".


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
- **After layer 5 (D396), built and fresh-checked:** `fsd take` brings a taken section's attachments (files under
  `attachments/<id>/`, records in `document.json`; the id kept by the same bytes, then the same file name, in the
  section; a new one from the ledger; one gone marked removed), and a section whose only change is its attachments is
  offered; a PM's attachment change is a repo change (`fileSections` entries gain optional `attachments`). `fsd check`
  marks a removed section's `specs/trace.yaml` entries `(removed S-0004) 4`, decision kept, and puts them back if the
  removal is undone before the commit (`FsdCarry`). Tests in `EmbeddedFilesTests` (4) and `FsdCheckTests` (1). Fresh
  check: Sonnet, about 107K; 2 defects fixed (a restored entry's number, a taken record's `sectionNumber`).

### Layer 6 · Word export (D378) — cut (D397)

Not built: the owner cut it on 2026-11-18 (D397). What it would have been, kept for the record:

- `fsd export <doc> --whole --format docx`: headings, numbers as written, tables and pictures, no markers.
- **Tests:** export → import gives the same sections. **Fresh check:** narrow, about 60K.

## After the layers

- Docs: `docs/spec.md` (import, versions, the truth map), `docs/cli-conventions.md`, `docs/tickets.md`; `CLAUDE.md`'s
  commands; `OVERVIEW.md` through `plain-overview`.
- The end-of-phase report (`docs/phases.md`), with the ripple list and the migrated benchmark clones.

## End-of-phase report

Written 2026-11-18, on `claude/fsd-source` at `7e34fac` plus this report's docs (not pushed; `main` is 358 commits behind).

### 1. What was built

The FSD in the repo is now the source of truth. After the first import the client's file never comes back (D376): the FSD
is Markdown chapter files the PM edits in git, every step reads them as committed, and a commit is the approval (D380).

| Layer | What it built | Commit | Fresh check (Sonnet, narrow, D158) |
|---|---|---|---|
| 0 · No switches (D381, D388, D390) | `StangentComponents`, `stangent components` and every `IsOn` gone; old keys in `stangent.yaml` ignored with a warning; `ai.enabled` kept (D24); `findings` and section approval removed | `3951830` | ~146K tokens: 2 defects (switch words left in help texts and docs; an unreachable `component_off`), fixed; no wrong result |
| 1 · Chapter files (D377, D378, D382, D384, D391) | `fsd import` writes `specs/fsd/<doc>/chapters/` with `<!-- S-0012 -->` markers, no `document.md`; `fsd migrate`; `fsd export --whole`; the writer's round-trip check | `4d1bf2f` | ~120K: 2 defects (the attached-files section's hidden number refused the import; a stray fence refused it), fixed |
| 2 · Read as committed (D380, D383, D392; `source-document` v4) | `FsdDocument.Load`: chapters, facts, ledger and attachment files as of HEAD, cached by tree hash; `document.json` loses `sections` and `markdown`, gains `importCommit`; `uncommitted` warnings | `8541c5b` | ~148K: 4 defects (facts, ledger and attachments read from the working tree; `fsd tables` crash and sheet match), fixed |
| 3 · `fsd check` (D380, D382, D384, D393) | ids for new headings, the refusals (damaged, repeated, unknown, old, moved markers; a marker on another section's words; rulings on a removed section), number warnings, the change list a commit approves | `dcbd450` | ~105K: 3 defects (a marker laid or swapped onto another section's heading passed), fixed |
| 4 · `fsd write` (D379, D386, D387, D394; `rulings` v2, `truth-map` v2) | the PM's words placed by code; "code is right" drafted by `stangent-fsd-writer` and checked by `fsd write-check` (quote word for word, cites real and inside the link); a written ruling becomes a confirmed match with `writtenFrom`; `fsd-write-run.sh` | `8c80ab7` | ~131K: 2 defects and a schema gap (`writtenFrom.cites`), fixed; no unasked or uncited sentence through |
| 5 · A new file compared (D385, D388, D395) | `fsd import` of a later file writes `.stangent/fsd/<doc>/compare/`, three-way per section against `fileSections`; `fsd diff`, `fsd resolve`, `fsd take`; approve, reject, promote, discard, `--partial`, `candidate/`, `sections/` removed | `d8766b6` | ~143K: 3 defects (a fallback base could offer reverting a PM edit; reread overwrote an uncommitted take's record; take could stop halfway), fixed |
| D396 · Attachments and trace marks | `fsd take` brings a section's attachments (ids by bytes, then name, in the section); `fsd check` marks a removed section's `specs/trace.yaml` entries `(removed S-0004) 4` | `c6acd93` | ~107K: 2 defects (a restored entry's number, a taken record's `sectionNumber`), fixed |
| 6 · Word export (D378) | **cut by the owner (D397)**: no client needs a Word copy now; `fsd export --whole` stays Markdown | `7e34fac` | — |

No fresh check was re-run after its fixes. Tests: 1,911 at layer 0's check, 1,970 at the last full run (D396), all
passing. Commands now: `fsd import`, `migrate`, `check`, `write`, `write-check`, `diff`, `resolve`, `take`, `export`,
`status`, `outline`, `show`; the plugin's `/stangent:rule` runs write, then check, then reads the chapter diff back.

### 2. Benchmark results

None run: phase fs changes where the FSD lives and who approves it, not what any step finds. Kavita's and Sonarr's
planted FSDs were migrated in tests, byte for byte (layer 1). **The benchmark clones themselves are not migrated**
(D398 (1)): `~/stangent-bench/kavita` and `~/stangent-bench/sonarr` both still hold `document.md` and their sections
in `document.json`; Kavita's `specs/fsd/` is untracked there, and Sonarr's `specs/` is in `.git/info/exclude` so
`fix start` sees a clean base. Until each is migrated and committed on a branch there, any FSD step on it stops with
`fsd_not_migrated` or `fsd_not_committed`. Left unchanged, as the owner asked; done when the next step needs it.

### 3. Decisions made or changed

D376–D388 (the design), D389 (the plan, its calls), D390–D396 (one per layer, with its calls), D397 (Word export cut),
D398 (closing calls). Changed: D131 (`document.md` was a generated copy; now gone), D120 and D121 (switches; rule 8 is
now D381's), D378 ("Markdown or Word" → Markdown), D389 (six layers built, not seven; clones migrated when needed).

### 4. Known gaps

- **No real client document with two versions** has been through the comparison (D56); only fixture `.docx` pairs.
- **No live AI run of `fsd write`:** the drafter has run only with fakes; the first is with D375's paid run, asked first.
- Listed and left (D393–D396): ids taken before chapters are written can skip ids if the write fails (never reused);
  `--removed` doesn't mark the id removed in `ids.json` (`old-id` guards it); a commit that skips `fsd check` leaves a
  deleted marker's rulings on no section until the next review; `write-check` judges "inside the link" by today's graph;
  a restated statement found anywhere in a changed section becomes a match; a moved section is moved by hand; a
  document from before layer 5 gets no update offers until taken by hand; take replaces an attachment record whole
  (a hand-added field goes); a throw midway through take leaves folders and records out of step.
- Features turned on by D381 that never ran on a real repo (D388): `truth-verdict`, `ticket-excel-text`,
  `follow-moves`, `schema-once`, `attachment-home`, `statement-update`, `table-facts`; `root-screens` and
  `route-render` only on the pilot.
- Code text still from before: the completeness problem line says "aren't in document.md"; `TruthList`'s comment
  still speaks of the `truth-verdict` component.

### 5. Impact on other phases (D20)

| Contract | Version | Provided by | Consumers now `needs review` |
|---|---|---|---|
| `cli-conventions` | 3 → 4 (no `components` field) | 0 | 1a, 1b, 1c, 1d, 2a, 2b, 2c, 3, 4, tm |
| `telemetry-event` | 2 → 3 (no component attributes) | 0 | the same, except tm |
| `source-document` | 3 → 4 (no `sections`/`markdown`, `importCommit`; later optional `fileSections`, `converter`, attachments, no bump) | 2b | 2c, 3, tm |
| `rulings` | 1 → 2 (`inFsd` gone, `writtenFrom`) | tm | tm itself (its front matter caught up only now, D398 (2)) |
| `truth-map` | 1 → 2 (`writtenFrom`, `written` counts) | tm | tm; Phase 3 reads `truth-map.json` for the bundle's `truth` (roadmap marks it; its front matter doesn't list the contract) |

`phases check` ok after this report. Nothing downstream was changed by this phase's consumers' own follow-ups yet.

### 6. Follow-ups

- Migrate each benchmark clone's FSD when its next step needs it (Kavita first, for D375), asked first (D398 (1)).
- Phase 3's review: the bundle's `truth` shows no `writtenFrom`; check a written ruling reads right in a bundle and a draft.
- tm's review: `fsd rule` on a written entry, and D375's measurement with a live `fsd write` on Kavita's rulings (paid).
- Two stale code texts above (one-line fixes).
- A Word export comes back only by a new decision, when a client needs one (D397).

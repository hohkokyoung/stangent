# The spec (Phase 2: contracts `reference-page`, `reference-lines`, `source-document` v4, `trace-links`; layer 5: `fsd-review` v2)

**Layer 4 note (D258, D342):** the reference pages are being replaced by each area's page (`docs/pages.md`). They retire one
reader at a time: old linking no longer reads them (cut in layer 7, D342); tickets still do (starting points and the bundle), so the
writer, the verifier and the `spec` commands below stay until a ticket run without them shows the points hold.

"The spec" is three layers (D52), each with its own check:

| Layer | Where | Written by | Checked by |
|---|---|---|---|
| **Reference pages**: what the code does, per screen, shared component and backend entry point | `specs/reference/` | AI (Claude Code subagents, D60) | code: schema, every citation in the graph and in the page's slice; a fresh verifier (D62) |
| **The FSD**: the documents the client agreed, converted to text once, then the source of truth (phase fs, D376) | `specs/fsd/<doc>/chapters/` | converted by code at the first import; after it, edited in git by the PM (or `fsd write`, `fsd take`); a commit approves (D380) | same file → same bytes; `fsd check` before each commit |
| **Links, findings, decisions** | `specs/trace.yaml` | the old link route (cut, D342: frozen); table facts by code; people decide | code: quotes word for word, ids, cited symbols, `decided_by` from git |

Everything above is committed: AI output is stored, not regenerated (D18). The graph (D25) and the AI steps' working
files (`.stangent/spec/`, `.stangent/fsd/`) are not.

## How an AI step runs (D60, D61)

Every AI step is the same small loop. The CLI holds all the logic; a driver only runs the subagent each step names.

1. `stangent … prepare` writes the step's input as one file (instructions, output schema, the evidence) and prints
   `result.next`: `{action, agent, prompt, output}`, or `action: none` when nothing needs AI (inputs unchanged, D18).
2. The driver starts the named subagent from the plugin with exactly that prompt. Its tools are `Read` and `Write`.
3. `stangent … check [--usage <claude json>]` checks the output by code and prints the next step. A failed check
   gives the subagent **one retry** with the specific errors; a second failure is recorded as failed.

Drivers: `/stangent:spec-generate` and `/stangent:fsd-review` in a Claude Code session, and
`plugin/scripts/spec-run.sh` for batches and CI (headless `claude -p --output-format json`, so tokens, cost, model and
time are recorded per step, with the model and effort it ran under (D161, `docs/foundation.md`); `--jobs N` runs pages side by side). The batch driver makes **one turn per call** (D66):
the step's input files go into the prompt, Claude Code runs with no tools and none of the user's settings, skills or
MCP servers (`--tools "" --setting-sources "" --disable-slash-commands --strict-mcp-config`), the output schema is
enforced with `--json-schema` (`next.schema`; `stangent spec schema <name>` prints it), and the driver writes the
structured answer to `next.output`, so `check` is unchanged and input hashes are the same in both modes. `--agents`
runs the plugin's subagents instead, each allowed to read and write only under `.stangent/`
(`--permission-mode dontAsk --allowedTools "Read(.stangent/**)" "Edit(.stangent/**)"`). Files
drivers share are written whole (temp file, then rename) under a lock. A section whose candidate pages have no recorded
version is reported as failed, never turned into a gap. `ai.enabled: false` stops every `prepare` with exit
code 4 (D24); `spec stale`, `spec cite`, `fsd import`, `fsd diff`, `fsd resolve`, `fsd take`, `fsd review-merge` and `trace check` are code only.

## What a bundle stores (D130)

A page writer's or page verifier's file under `.stangent/` holds a pointer on its first line and what's unique to it. The one copy of the instructions and schema is in the program. `stangent spec assemble <file>` puts them back, byte for byte; the driver does this just before each call, and `--in-place` does it for a subagent that reads the file itself. Input hashes are taken on the full text, so saved pages and links stay valid, with `SavedWorkTests` and `SharedHeadTests` pinning it. Only the first line is ever read as a pointer. `spec bundle <page>` prints the full text, for people.

`schema-once` (always on since D381): batch runs send the schema through Claude Code's schema option only, not also in the text (J.4), about 2.5K characters a page. No hash moves; what the AI reads changes, which was why it was off until D381.

## Reference pages (2a, D54, D55)

- `stangent spec pages`: every page the graph calls for.
  - **Screens:** one per frontend route, id `page:<route>`; a server-rendered action (GET, returns `View(…)`) is a screen too.
  - **Shared components:** a component in 3 or more screens' slices gets its own page; screens link to it, not into it.
  - **Backend pages:** every endpoint (screens stop at the endpoints they call) and every event or command handler.
    Id: the entry point's graph id. Files: `specs/reference/{screens,components,backend/<project>}/<name>-<hash6>.{json,md}`.
- **The slice** (D54): the walk forward from the entry point, along the lines a change travels, to `spec.depth` lines
  (`spec.backendDepth` from an endpoint or handler). Side effects one step back: handlers of events it publishes,
  tables it maps to. A point with `spec.hubCallers` or more callers is included but not walked into. ❓ blind spots in
  the slice are appended to the page by code. Settings and defaults:
  `spec: { depth: 6, backendDepth: 4, splitAtEndpoints: true, hubCallers: 10, maxBundleChars: 260000 }`.
- **The bundle** (`stangent spec bundle <page>` prints it, without the previous version): instructions, schema, the slice's symbols with ✅/🟡, key
  lines, the source of each method and component, what it couldn't see, and the page's previous version. Its input
  hash covers everything but the previous version. Over `maxBundleChars` → listed as failed, never cut.
- **The writer** (`stangent-page-writer`) returns sections `shows`, `actions` (with the `endpoints` they call), `rules`
  and `sideEffects`; every statement cites symbol ids. Code gives them ids S1…, A1…, R1…, E1…. While a run is under way, the writer and the verifier see
  them numbered by place, as before; the permanent ids go on when the page is recorded.
- **Statement ids (2.3, D118)** stay with their statements. A page's ids at adoption become permanent, so nothing already cited moves. Each page has a ledger beside it, `<path>.ids.json`
  (`docs/foundation.md`). When a page is recorded again, code matches by exact signals, with no score:

  | The new statement | Its id |
  |---|---|
  | The same words, whitespace normalised, in any section | Kept. An id's letter says where the statement began. Repeated words pair off in order, the same section first. |
  | Other words, the same section, exactly the same cited symbols, alone on both sides among what's left | Kept. Its text changed, so a decision made on it drops back (`decided_on`, below), even when a quote still fits. |
  | Anything else | The next unused number in its letter. |

  An old id nothing claimed is marked removed in the ledger and never comes back. A link citing it isn't re-pointed: its statement is gone, so `trace check` lists it in `staleQuotes` and `retiredStatements`, and `--fix` drops its decision.
  A verifier's finding is recorded on its statement's final id, or `(removed in the fix)`.
- **Line citations (2.3, D118; `schemas/reference-lines.v1.json`):** code fills them from `graph where`, never the AI. `<path>.lines.json`, beside the page, gives each cited symbol its `file`, `start` and `end`,
  and the file gives the graph's `commit`. Nothing hashes that file, and it changes nothing in the page's JSON. The Markdown lists them under "Cited symbols".
  - Citations are per file, on `graph where`'s answer. A symbol in a dirty or untracked file, in a file the commit doesn't hold, or no longer in the graph, has `why` instead, and the page says so. Every other symbol keeps its citation. So does a symbol the graph holds only by file: it keeps `file`, with no lines and a `why`.
  - A graph with no commit, or one too old to say whether it was clean, writes no citation at all: the file's `note` says why, and so does the page.
  - A partial type's lines cover its first part only. Its `note` says so, in this file, on the page beside the citation, and on any evidence that copies the lines.
- **`stangent spec cite [page]`** fills citations for pages already recorded and adopts their ids. Code only, and it needs the graph, not the source. It doesn't touch a page's JSON, so nothing goes stale. It renders the Markdown again, and there a link goes only to a page that's recorded. It reports `citations` and distinct `symbols`, with and without lines, and `why`.
- **The verifier** (`stangent-page-verifier`, fresh context, bundle and finished page only) flags statements the slice
  doesn't support: `contradicted` (with the contradicting line, which code checks is in the slice: at least 12
  characters, and two tokens or a whole line (D103), so a fragment like `return` is no evidence; otherwise the finding is dropped) or
  `unsupported`. It gets one retry when its output is missing or breaks the schema. The writer then fixes or rejects each finding once; a "fixed" statement must
  change, a rejected `unsupported` finding needs the supporting line. Findings and outcomes are recorded on the page.
- **Recorded:** the page JSON (`schemas/reference-page.v1.json`) and its Markdown, rendered by code: the commit, the
  input hash, the slice size, the writer's and verifier's model, tokens, cost and time, and retries.
  `specs/reference/failed.json` lists pages that failed, with why; they're not retried until their inputs change
  (`--retry-failed` forces it).
- **Keeping current** (D55): `stangent spec stale [--diff base..head] [--format md]` lists pages whose input hash
  changed (with the changed files in each slice and the FSD sections linked to each), new pages and removed ones, as a
  PR comment. On merge, CI runs
  `spec-run.sh --stale` and opens a spec-only PR (see `plugin/ci/spec.yml`).

## Pages when they're needed (I.1, D131)

The normal way to make pages is for what needs them. On Sonarr 113 pages of 718 ever ran. `stangent spec needed --ticket <key>` lists what that ticket's saved bundle says it's missing, and `spec-run.sh --for-ticket <key>` makes just those (the `--doc` side went with the link route, D342). `--stale` now means written pages whose code changed: `--new` makes every page never written, and `--all` is every page, as before. No prompt or hash moves.

## Only the statements a change touches (I.2, D131)

A changed line regenerates the whole page and its verifier. With `statement-update` (always on since D381; it was off by default because it changes what the writer gets), `<page>.shape.json` holds a hash of the code shown for each point in the slice. A stale page's statements whose cited points are all unchanged are kept by code: they go back on word for word, keep their ids, and the writer is told not to return them. The writer returns the rest, and anything new. The verifier reads only what isn't kept; if nothing is new it doesn't run. The input hash is the same either way, so saved pages stay valid. The writer still reads the whole bundle, because a new behaviour may be anywhere in it, so the saving is what it writes and what the verifier reads.

The whole page runs for a first version, a `page-wrong` mark, new instructions or slice settings, or no shape on file.

## Code that only moved (I.4, D131)

A page's input hash covers line numbers and file paths, so a line added above a method, or a C# file moved, makes the page stale, and a paid run writes the same thing. With `follow-moves` (always on since D381), a page gets `<page>.shape.json` (contract `page-shape` v1): a hash of what it was written from with where the code sits left out, the same instructions and schema, points, lines and code. If that's unchanged, `spec prepare` moves the page on with no AI: its input hash, commit and citations change, and its statements don't. `spec stale` lists it under `moved`, not stale; `spec read` doesn't call it behind; `stangent spec follow` does them all at once and starts a shape for current pages that have none.

Anything else stays stale: a change inside the code shown, a new line in the graph, a change to the instructions. A TypeScript file that's renamed changes its symbols' ids, which hold the path, so it stays stale too, and the page runs. A page with no shape on file can't be compared, so it runs once. Off by default because it rewrites a saved hash. No number decides anything: the same or not.

## Navigation (T.7, D131)

A page's Markdown links to `specs/reference/index.md` at its top and bottom, and its footer says it's generated and how to correct it. The index lists every written page by kind and group; it's rewritten whenever a page is written, or by `stangent spec nav`. `stangent spec cite` puts the links on pages written earlier. Only people read these: no step reads a page's Markdown, so no prompt or hash moves.

## Every page accounted for (T.3, D131)

`stangent spec coverage` lists every screen, shared component and backend entry point the graph calls for, per kind: written, failed, not written yet, or excluded with a reason. `--strict` exits 1 if any is neither written nor excluded. A page not written yet isn't a fault in itself, since pages are made as needed (I.1): `--strict` is for a project that wants them all. It reports what the graph knows: what the graph can't see isn't here, and is counted where it was read (`docs/code-graph.md`).

`stangent spec exclude <page> --why "..." --by <name>` says a page isn't one, for a tooltip `root-screens` took for a screen, say. It goes in `specs/page-exclusions.json` (contract `page-exclusions` v1), and needs `page-exclusions` on (the default since D373). An excluded page isn't in `spec pages`, `spec stale` or a section's candidates, and nothing writes it. Other pages' slices and hashes don't move, because exclusions are taken off last. A page already written and then excluded shows as `removed` in `spec stale`, like any recorded page the graph no longer calls for. `spec include <page>` takes it back. An exclusion for a page the graph no longer calls for is listed as `gone`.

## A person's correction to a page (T.1, D131)

`page-wrong` asks for the page to be written again, and the writer may well write the same thing. A correction sticks. On by default since D373 (`page-corrections`; it was off because a corrected statement is what link judges then read, and that route is frozen, D342): it acts only on a correction a person approved.

- `stangent spec correct <page> <statement> (--text "..." | --remove) --why "..." --by <name>` records it in `specs/page-corrections.json` (contract `page-corrections` v1), as `proposed`.
- `stangent spec correct-approve <C-id> --by <name>` is a person's yes. The page's JSON and Markdown say it from then on, marked in the Markdown. Its input hash doesn't move, so no AI run follows, and a `page-wrong` mark on that statement goes back to the person who made it.
- `stangent spec correct-reject <C-id> --by <name> --why "..."` refuses a proposal or withdraws an approved one. A withdrawn one is left off when the page is next written.
- Every time the page is written, code puts approved ones back on: by the statement's id, which stays with a statement (D118), then by the words it was given for. The writer isn't told, so no prompt or hash changes. If neither is on the page, it's `notApplied`, the step warns, and `spec corrections` shows it for a person. `codeMoved` there says the code changed since the approval.
- A correction changes or removes a statement. It can't add one: for something missing, `page-wrong` still applies. No AI anywhere in this: a person is the check (D18).

## Checks where pages are read and written (T.4, T.5, T.6, T.8, D126)

All code, no AI, and none changes a prompt or a saved hash.

- **`stangent spec read <page>`** (`--format md` for the page itself) prints a recorded page. When its inputs have moved on from the code, there's a warning on stderr, in the result, and on top of the Markdown. A ticket bundle has always had `stalePages`. A Markdown file opened directly can't warn: writing the warning into it would change it with every commit. `spec stale` in the PR comment covers that case.
- **`stangent spec content-check`,** run in CI. Broken: a Markdown link in a page to a file that isn't there, a link in `trace.yaml` to a page, statement or section that isn't there. Repeated: a page id, a statement id on a page, a section id in a document, a link id, an id in the requirements. A link to a page the graph calls for but no one has written is counted apart (`linksToPagesNotWritten`), since pages are made as needed. A retired statement and a removed section keep their links on purpose; `trace check` lists those. With no graph, a page not written yet counts as broken, and it warns.
- **`stangent run-check before|after --snapshot <file>`:** `spec-run.sh` and `ticket-run.sh` wrap each run in it. By git: the commit, and each changed or untracked file with a hash of its content, leaving out `.stangent/`, `specs/` and `stangent.yaml`. A change is `APP FILES CHANGED` on stderr and exit 1. It can't see git-ignored files such as build output, or say which job did it, and outside git it says it didn't check. A `--prove` run isn't checked: bug proof builds and runs the app (D75).
- **`plugin/scripts/read-only-hook.sh`** stops an analysis step writing outside `.stangent/` or running a command, through a `PreToolUse` hook. One-turn calls have no tools, so it matters for `--agents`, the file finder, and agents started through the plugin's commands, which `plugin/hooks/hooks.json` catches by their `agent_type`. The bug reproducer is the exception (D75). Only tested by feeding the script hook input: no real Claude Code has run it, and the `agent_type` field is as the assistant remembers it, not checked here.

## The FSD (2b, D13, D16)

- `stangent fsd import <file> [--id <doc>] [--flat] [--accept-changes] [--allow-label <name>]` takes `.docx`, `.pdf`, `.xlsx` or `.xlsm` (D111, D123), by code only: the same file gives the same bytes, and
  what can't be brought in exactly is listed in `problems`, never dropped. `.xls` is `unsupported_format`, and says to save it as `.xlsx`.
- **The chapter files (phase fs layer 1, D377, D378, D382, D384, D391).** A first import writes the FSD as `specs/fsd/<doc>/chapters/`, the files the PM edits, and no `document.md`: one Markdown file per top-level chapter, cut at its sub-headings when over `fsd.exportMaxLines` (1500) as `fsd export` cuts, named with its section's permanent id (`01-s0001-home.md`, `02-01-s0003-pay.md`), with the text above the first heading in `00-front-matter.md`. Every heading ends with its permanent id, hidden: `### 3.1 Series page <!-- S-0012 -->`. A heading whose words start with a number the import didn't take as one says so (`## 2023 Roadmap <!-- S-0012 no-number -->`), as does one the import numbered and whose words show no number (the attached-files section: `<!-- S-0012 number=7 -->`). A line that isn't a heading but would read as one gets a backslash (`\# of retries`). No converter writes code fences, so none are read. The files hold the document's lines exactly: joined with a line break, markers and those backslashes off, they are the converter's Markdown byte for byte, and the import checks that they read back as `document.json`'s sections before anything changes (`chapters_out_of_step` otherwise, a stangent bug). The result has `chapters` (`dir`, `files`, `overLimit`) and `next`: read the chapters and commit them, since the commit approves the import (D380).
- **Sections are read from the committed chapters (phase fs layer 2, D380, D383, D392; `source-document` v4).** `document.json` holds only the import's facts: the original, problems, tables, pictures, attachments, labels and `importCommit` (HEAD when the import or `fsd migrate` wrote it: the commit its chapters were written on). It has no `sections` and no `markdown`. Every step reads the sections through one reader (`FsdDocument.Load`) from the chapter files **as of HEAD**, with HEAD's `document.json`, `ids.json` and attachment files, through git, cached in `.stangent/fsd/<doc>/chapters-cache.json` by the git tree hash of `specs/fsd/<doc>/chapters`; a section's `attachments`, `attachmentLines` and `contentHash` are worked out from the document's `attachments` by permanent id. So an uncommitted edit is never read, and committing it is what makes it count. Refused: a document still holding its sections in `document.json` (`fsd_not_migrated`, naming `fsd migrate`), a folder that isn't a git checkout (`not_git`), chapters not committed yet (`fsd_not_committed`; `fsd status` says to commit), and committed chapters where one id sits on two headings or names an id `ids.json` never gave or marks removed (`invalid_fsd`, D389 call 1, D392), or where a line holds a marker that doesn't read as one (`<!--S-0012 nonumber-->`, `<!-- S-12`, a marker off its heading: `invalid_fsd`, D393); an emptied ledger says to restore it from git. `fsd tables` lists a table a committed edit took away under `tablesNotFound`. A heading committed with no marker reads with no id, and a warning says to run `fsd check`. A step over every document (`trace check`, coverage, `fsd sections`) leaves out one it can't read, with a warning that says why. A later file and `fsd reread` compare with the committed version too (D385).
- **`stangent fsd migrate <doc>` (D389, D392).** A document imported before phase fs: its `document.md` is cut into chapters as an import would and removed; ids are added to sections that had none; `document.json` drops its sections (v4). One imported during layer 1 already has chapters, and only drops the sections. Refused outside a git checkout (`not_git`), with uncommitted changes under `specs/fsd/<doc>/` (`uncommitted_changes`, listing them), and when `document.md` was edited by hand (`document_edited`: that edit never reached a step, so it's undone first). A migrated document is `unchanged`. Kavita's and Sonarr's benchmark FSDs migrate to the same text, byte for byte.
- **`problems` are data (`source-document` v3, D121).** Each has `kind` (`comment`, `completeness`, `tracked-changes`, `flat`, `numbering`, `element`, `page-text`, `running-line`, `table`, `heading`, `contents`, `bookmark`, `image`, `other-direction`, `white-text`, `symbols`, `before-first-section`, `page-join`, `outside-body`, `attachment`, `hidden`, `shared-workbook`, `merged`, `formula`, `number-format`, `large-workbook`, `protected`, `not-read`, `sheet`, or `other`) and `detail`, the line a person reads, word for word what v1 and v2 held. Where the line says, it has `sectionNumber` and `section` (the permanent id, once known), `page` or `line`. A comment adds `author`, `state` (`open` or `resolved`) and `text`. Converters still write lines, and one reader, `ImportProblems`, takes them apart, for new imports and old documents alike. A line no rule knows is `other`, never dropped. v1 and v2 documents still load; the next import of the same file upgrades `document.json` and changes nothing else. A new file's problems get their `section` in its comparison (D385).
- **The verdict and the report (D122).** Each import's result has `verdict`: `ok` with no problems, `ok-with-warnings` with any, `failed` when the file stopped (no_structure, no_text, a lock, a label, damage, unsettled changes), which is in the error envelope's `result`, since nothing was written. `specs/fsd/<doc>/import-report.md` is made by code from `document.json`: the verdict, the file, and the problems grouped by kind with counts, what didn't arrive (`completeness`) first. A new file's is in `.stangent/fsd/<doc>/compare/`, and `fsd take` rewrites the document's own from what it took. `result.report` names it. No step reads it back, so it's in no hash.
- **Reading the original again (D122, A.1).** `stangent fsd reread <doc> [--allow-label <name>]` reads the archive's original with this build, so converter fixes reach earlier imports. The bytes must still hash to what's recorded (`archive_file_missing`, `archive_file_changed`). It compares text and section shape, not the file's hash. Different means a comparison as a new file gets (below), `reread: true`, the chapters unchanged. The same means `unchanged`; if the build has more to say, `problems` and the report are updated, `problemsChanged: true`, since problems are in no hash. A flat import stays flat, accepted tracked changes stay accepted and an allowed label stays allowed, with who allowed it.
- **Ids for tables and pictures (D122, A.6).** `document.json` has `tables` (`T-0001`, section, place in it, rows, columns, a hash of its text) and each picture has `id` (`F-0001`) and the first section that shows it, from the document's ledger. They're worked out from the text the conversion gave, so the chapters and the sections don't change, and no hash reads them. A picture is its file, named by content. A table keeps its id by the same words in the same section, then the same words anywhere if unique, then the same place in the same section. An id nothing claims is marked removed and never comes back. An older document gets them on its next import, the same file included. Attachments have `A-` ids, below.
- **First-import approval (D122, A.9), removed by D388 (phase fs layer 5):** committing a first import's chapters approves it (D385).
- **The steps (D121).** `IImportFormat` in `src/Stangent.Spec/Fsd/ImportPipeline.cs` has safety checks, convert, comments, extract files and a completeness check; `ImportPipeline.Run` runs them in order, and the result's `steps` says what each did: `result` is `ran`, or `not-for-format` with why, so a missing step can't read as an empty one. Safety and convert can stop an import. Word and PDF read comments and check completeness in the walk that converts, so their steps report what it found; a PDF has `comments: not-for-format`, because its annotations aren't read. Excel fills in the same interface: its completeness check is a second pass reading each row back from the table.
- **`.docx`** converts with the Open XML SDK, in C#: headings (Word's automatic numbering worked out, numbers typed into headings, or
  plain), numbered and bulleted lists, tables, images with their captions. Inline formatting is dropped, because quotes are matched
  against this text. The same principle as a PDF: what can't be brought in exactly is listed in `problems`, by kind and section (D116).
  - **A number typed on a chapter heading** stays a number across a gap, because taking a chapter out leaves one, and `problems` says one is skipped. It's read as words, with a plain heading's id, when it's more than 10 past the chapter before (`2023 Roadmap`), when a lower-case word follows (`24 hour clock`), when a later chapter's number is at or below it (`3 Ways to pay` before `2 Pricing`), or when nothing numbered comes before it and the next number doesn't follow it. `1 Intro` then `3 Ways to pay` has the same shape as `1 Orders` then `3 Checkout`, so it reads as a number, and the skip is what tells a person.
  - **Tracked changes** (inserts, deletes, moves, including in a footnote, on a paragraph mark or a table row) stop the import: `docx_tracked_changes`, with a count, nothing written. The document isn't settled. Format changes don't count: they change no words. `--accept-changes` imports the accepted view: inserted text kept, deleted text gone, moved text once, a deleted paragraph mark joining the two paragraphs, as in Word. `problems` says how many were accepted. It's refused for a PDF. A re-import of a file already recorded stays `unchanged`, with or without the flag.
  - **Hidden text** (`w:vanish`, on a run or through a style, by Word's rule that a character style and a paragraph style cancel) is left out and listed with its word count, and so is a hidden table row. A hidden paragraph mark alone hides no words, only the line break, so its visible text stays, as its own paragraph, and is listed.
  - **Struck-through text** stays, listed, since a strike can mean a retired rule and only a person can tell.
  - **Comments** stay out of the text. Each is one `problems` item of kind `comment`, with `author`, `text` and `state` from Word's own resolved flag, in the order of their anchors. Its `detail` is the line `comment: section "4.2" by "Jane" open: "Is it 3 or 5?"`. One with no anchor left names no section.
  - **Footnotes and endnotes** come in at the end of their section, as `[Footnote 1, after "the last five words"] text`, numbered in reading order, which can differ from the number Word prints. The sentence itself gets no mark, because quotes are matched against it.
  - **Other things a file can hold:** a text box keeps its text where it's anchored, once, listed. SmartArt keeps its words as a list, listed, since its order isn't always the drawing's; in a table it's only listed. Embedded files (by program id, such as `Excel.Sheet.12`), charts, equations, symbols, ActiveX controls, old-style and linked pictures and `altChunk` documents aren't read, and are named with their section. A content control keeps its content, but a prompt still showing ("Click here to enter text") is left out and listed. A field keeps the text Word last saved, listed unless it's only a link, page number or contents. An `mc:Fallback` is a copy for old readers, skipped, only when an `mc:Choice` sits beside it; with none it's what the file holds, and it's read and checked. Page headers and footers aren't imported: one line with their word count.
  - **Tables:** a nested table keeps its words and is listed as flattened. A cell merged down rows repeats its value in each row, and is listed; text Word doesn't show in such a cell is left out and listed. A cell merged across columns keeps its value in the first column and blanks after it. A row that starts part-way across the grid keeps its cells under the right columns, and rows and cells inside a content control are read.
  - **Completeness check (D18), on every import:** the check reads the file apart from the converter, by element name in any namespace, every word in a text-bearing element. Per section, those words, minus what a rule removed, plus what the converter adds on purpose (numbers, list marks, table rules, footnote labels, repeated cells), must equal the words in the converter's Markdown (what the chapter files hold, markers off). A mismatch adds a line, `completeness: section 4.2: 3 words in the file aren't in document.md ("…")` (the line still names `document.md`, the file it was written for), and never fails the import, so a kind of element nobody thought of can't vanish silently. A word it can't see, such as text inside a picture, it can't check.
- **`.pdf`** reads with PdfPig. Principle: a false sentence is worse than a broken one, and a listed problem is better than either; when the code can't tell, it doesn't join and doesn't drop. The text is PdfPig's
  content-order extractor's, or its own rule run over only the letters read (below) when a page has others. Words are never changed (no de-hyphenation, no fixing).
  Wrapped lines join with single spaces. Paragraphs split on line spacing against the document's usual pitch, or where a line ends a sentence with room left for the next word; room alone proves nothing. A line after a list item joins it by the same rule, unless it starts with a marker of its own.
  A paragraph runs on over a page break only with strong evidence: the last line is body text at the body's size and left edge with no end mark, and the next page starts in lower case; never from or into a cell's paragraph or the text of a table kept as plain text. One `problems` line counts these joins, with their pages.
  - **What's read:** the page's main text direction, so a page turned as a whole reads normally. Text in other directions (watermarks, margin notes, diagram labels) is kept out of the text and listed once per pattern with its page count. Only what is certainly never painted is removed and listed per page with a word count: render mode 3 and text off the page, since a hidden rule mustn't read as agreed text. Invisible text over a full-page image is an OCR layer: kept, and listed as not checked. White text is kept and listed in one line, with its word count and pages, because whether anything dark lies behind it can't be told; a colour that can't be read counts as visible.
  - **Reading order, one rule:** the drawing order, unless it goes against position (back along a line, or up the page without moving wholly right to a new column). Then that
    page is read by position (columns, then top to bottom, left to right) and listed as order uncertain. Tables count: one takes its place by position, in the same order as the prose.
  - **Sections, in this order; fonts are never used to guess headings:** (1) bookmarks, depth giving the level; one with no page inside the document is listed and not used, its children still
    count. (2) The printed contents near the front: `title ..... page` lines, up to where page numbers fall (a list of tables or figures). An offset is worked out by code and **every** entry must then be a
    whole line on its page; level comes from the number's parts, an unnumbered entry is level 1, and `problems` says sections may be coarser than the real structure. (3) Otherwise, if any entry fails, or if more than one offset fits: `no_structure`, nothing written.
  - **Headings** must be a whole line, or whole wrapped lines; a line is never split to make one. The bookmark's position chooses between several matches; with no position the first is taken and listed as ambiguous.
    A title with no whole-line match is listed, its text stays as it is, and the heading goes where the page's remaining text starts. Text before the first heading is in no section (as for `.docx`), and for a PDF its word count is listed.
  - **Running headers and footers:** a line that stands apart from the text, at one distance from the top or bottom edge, on most pages (or most odd or even pages), and whose text is identical or differs only by a number following the
    page, Arabic or Roman. A number that doesn't track the page isn't a page number. A section's own title and lines inside a table grid never count. One `problems` line per pattern, as it reads, with its page count. A line that repeats but doesn't stand apart is kept and listed. A real rule that fits the pattern ("Day 6: the supplier must confirm…") is removed: it's listed, and only its content could tell.
  - **Tables** come from the page's drawn lines: a cell is a grid region closed on all four sides, and a merged cell keeps its place with blanks for the rest. A fill thicker than a rule is shading, not a line.
    Words are placed whole, by the baseline, never by a letter's glyph box, which some embedded fonts report wrongly. A grid that's seen and not emitted is listed (it frames the page, has no text, or has text in only one row or column). A lone row or column, or a grid with text in one row or column, keeps its cells' text apart in paragraphs, in the order drawn and apart from the prose around it, and is listed with its first words and shape, since its columns aren't kept; cells drawn line by line across a row alternate line by line. A single box round some text is prose. Other drawn lines that suggest a table are listed per page as a possible table: parallel rules a row apart with text between them, or a divider with text on both sides. A lone rule under one line of text is an underline and doesn't count; two or more with text between them always do, whatever their widths.
  - **Checks.** A table stands only if every cell is word for word (white space normalised) in the page's own text, no word or run of text crosses a rule, no word in the grid's drawn extent is outside a cell, and each cell's words are drawn in order; otherwise it's plain text and listed with the reason.
    Each page's read letters are also read a second way, by position with the extractor's own white-space rule; if the two readings give different words, the page is plain text and listed. The words emitted must equal the page's own, minus removed running lines; the plain-text fallback is checked too, and if that fails the page is its own lines, and listed.
    **The limit: every check rests on PdfPig's own letters, so none can catch altered letters. That would need a second PDF reader.**
  - **Images** are kept in reading position, named by content hash. One that can't be decoded is saved as raw bytes and listed. One `problems` line counts them: no AI or code reads them, and rules can sit inside diagrams.
  - **Symbols:** only clear bullets become `- `; a tick, an empty box or an empty circle stays as drawn, and a line starting with one starts a new line. Glyphs with no standard letter (a symbol font's private range, the replacement character) stay as drawn, with one `problems` line counting them, since quotes depend on them.
- **`--flat`** imports the whole document as one section, id and title `(whole document, no sections)`, with a `problems` line saying it has no structure. A re-import that changes
  between `--flat` and not is a changed import, so it is compared like a new file (below).
- **Errors, with nothing written:** `no_structure` (a PDF with none of the above, or a `.docx` with no headings), `no_text` (no page has text), `pdf_locked` (any encryption, even one that opens without a password; protection is never worked around), `invalid_pdf` (PdfPig couldn't read the file, including an outline that loops or nests impossibly deep; usually damage, but it can be a valid file PdfPig can't handle), `invalid_docx` (damaged, or not really a Word file), `docx_locked` (a Word file with a password; it says how to get a copy that can be read), `document_labelled` (a sensitivity label nobody allowed), `docx_tracked_changes` (a Word file with tracked changes and no `--accept-changes`). A `.docx` with no headings lists, in the error's details, up to its first 20 bold-only lines of at most 15 words that don't end like a sentence, by paragraph, and says it never guesses (A.4).
- **Sensitivity labels (D122, A.3).** Word's `docProps/custom.xml` `MSIP_Label_*` properties and `docMetadata/LabelInfo.xml`, and the same properties in a PDF's information or XMP metadata. One switched off or removed doesn't count. Any other stops the import, with nothing written or archived, unless `--allow-label` names it by name or id. What was allowed goes in `labels` in the document's record, with `allowedBy` from git. Protection is never bypassed, and a label is never removed. A ticket attachment with a label isn't read, and `notConverted` says why. **Known gaps:** a label in another product's own property, or in XMP that doesn't parse, isn't seen, and a same-file re-import doesn't look again until `fsd reread`.
- **A table of contents (I.11)** is left out and listed: a content control Word marks as one, a `TOC` field across its paragraphs, or paragraphs in the `TOC1`–`TOC9` and `TOCHeading` styles. It's the headings and page numbers again, and it would land before the first heading as text. The style names are what Word writes in English; a table in a translated style outside a field or control stays in as text. Sonarr's FSD has none, and its text is byte-identical.
- **Known Word gaps:** never yet run on a file saved by Word itself, only on hand-built files and Sonarr's planted FSD, which a script wrote. A tracked merge of table cells (`w:cellMerge`) counts as a change but isn't applied. White text is kept and not listed. A style from a table doesn't count for hidden or struck text. A hidden run that splits a word leaves its two halves joined. A file imported before this check stays `unchanged`, with no new lines, until its bytes change. Ticket attachments, `.docx` and `.pdf` alike, go through the same steps (D121) and never stop the pull: the accepted view, flat when there's no structure, and `notConverted` when one can't be opened. `problems` and `format` stay in `ticket.json`, not in the bundle, which has no place for them. A PDF's `kind` stays `other`, since `ticket-bundle` has no other value for it.
- **Known PDF gaps:** a table split across pages stays as separate pieces; images are unread; no OCR, so a page with no text is listed and skipped; a title on a page doesn't prove it's the heading there. Fully borderless tables can't be detected without guessing: they flatten into text, unlisted. Tables ruled only across flatten into text too, and are listed; underlined lines close together are listed the same way, as a false alarm. Two columns drawn row by row can interleave. A word can split where PdfPig reads a gap in it. A capitalised sentence over a page break stays split, and a footnote at body size can join a lower-case next page (the join is listed). Near-white and fully transparent text is kept and not listed. Text covered by a shape isn't detected. Table rows drawn apart from each other lose their columns but keep their words in order. Text in another direction is listed with a page count, not its page numbers. Rotated column heads leave a table's header cells blank: their text is listed as another direction. A list item's label can read as a sentence's start, or the reverse, where nothing else marks a list. A page tree nested about 100,000 levels deep overflows PdfPig's stack as the file opens and ends the process (nothing is written): it can't be guarded before opening, and it takes a hostile file.
- **Excel (`.xlsx`, `.xlsm`; D123, A.5).** By code with the Open XML SDK. **No real Excel FSD has been seen:** everything here has run only on hand-built workbooks, none saved by Excel.
  - **Sections:** each visible worksheet is a level-1 section titled with its name, in workbook order, with a permanent id. Its `id` is the name (`idKind: path`). Headings inside a sheet are never guessed, and `--flat` is refused. A chart sheet, or a macro or dialog sheet, isn't a worksheet: it's named and has no section.
  - **Content:** one table per sheet, its first row the table's header because Markdown needs one, not because it's been judged a header. Empty outer rows and columns are trimmed; empty ones inside stay, as empty rows. A cell's line breaks become a space and a bar is `\|`. A merged range repeats its value in each cell and is listed; text under a merge, which Excel doesn't show, is left out and listed.
  - **Values:** what's stored. Shared strings and inline strings, numbers as stored (`1.10` stays `1.10`), `TRUE` and `FALSE`, error values as stored. Built-in date formats (14 to 17, 22, 27 to 36, 50 to 58) give an ISO date, and built-in times (18 to 21, 45, 47) a time, on the 1900 or 1904 calendar. A date Excel counts and no calendar has (day 60 of 1900) keeps its number and is listed. Other built-in formats show the raw number silently. A custom format shows the raw number and is listed by format, with a count and first cell, so a custom date format reads as its serial number. A formula shows its cached value and is listed with its text, once per shared or array range. One with no cached value is listed, shows empty and is never computed.
  - **Left out and listed (`hidden`):** hidden and `veryHidden` sheets, hidden rows and hidden columns, each with its count of cells holding text.
  - **Named, not read:** notes and threaded comments as `comment` problems, the shape a Word comment has, with the cell at the start of `text` (Excel's copy of a threaded comment for old readers isn't listed twice); charts, pictures, shapes with text, pivot tables, page headers and footers (`not-read`); external links, with their target; macros, by module name, from the project's OLE directory, never run.
  - **Stops:** `xlsx_locked` (a password; it says how to get a copy), `invalid_xlsx`, `no_text` (no visible sheet holds a cell with anything in it, whatever hidden sheets hold), `document_labelled` (as A.3, the same two places), `xlsx_too_large` (a sheet spanning more than 20,000,000 cells, merges included, or a file past the unpacking limits below), `xlsx_tracked_changes`. **A shared workbook's recorded revisions stop like tracked changes:** its cells already hold every revision, so `--accept-changes` reads them as saved and says so, and that answer holds for `fsd reread`. With a revision history that shows no change, there's no accepted state to compare, so it's listed (`shared-workbook`) and read as saved.
  - **Listed and readable:** a protected sheet or workbook structure (`protected`).
  - **Very large:** `fsd.excel.largeSheets` (50) and `fsd.excel.largeRows` (100000), `large-workbook` in the report. Stated settings, not measured, and it still imports.
  - **Completeness:** each stored cell is checked against the rendered table, read back row by row, or against a listed reason. A mismatch is a `completeness:` line.
- **Files inside a document (D123, A.6, A.7).** From `.docx` (`word/embeddings/`, packages and OLE objects), a worksheet's parts and files attached to a PDF. Each gets a permanent `A-` id from the document's ledger, and what it came to goes under `specs/fsd/<doc>/attachments/<A-id>/`. The file as it came goes to the archive (I.7, D16), at `<doc>/attachments/<hash 12>-<name>`, and its entry under `files` says where in `archived`, keeping the same `file` and `hash`. So does one nested at any depth. A document imported before this keeps its `original/` in the repo until `fsd reread`. `document.json`'s `attachments` holds each one's `fileName`, `from`, `hash`, `bytes`, `kind`, `reading`, `why`, `section`, `sectionNumber`, `chars`, `files` and `problems`; its section lists it in `attachments`, with a generated line in `attachmentLines`, shown by `fsd show`. **The chapters and a section's `text` don't change.** The report has a section for them, and each has an `attachment` problem, so a document holding one reads `ok-with-warnings`.
  - **What it comes to:** Excel gives one CSV per sheet, whose line N is row N of the sheet (a hidden row is an empty line; hidden sheets and columns are left out and listed in `problems`). Word and PDF give Markdown through the same import steps. A zip is unpacked and each file read the same way. A part's name can be anything, so one that says nothing goes by what it holds. An OLE object gives up the file in its `Ole10Native` or `Package` stream. Everything else is kept and listed, with why: other formats, `.xls` and `.doc`, a locked file, a scanned PDF, damage. PowerPoint isn't decided, so it's kept and listed.
  - **It never stops the import.** `reading` is `converted`, `kept`, or `withheld` for a sensitivity label nobody allowed: that file isn't copied into the repo at all (the archived original still holds it), and `--allow-label` applies to it as to the document.
  - **A file attached to a PDF has no section:** the PDF names no place in its text for one, and none is guessed. A change to one shows under `attachmentsWithNoSection` in the diff, since it's in no section's hash. The same goes for a file nothing in a Word body points to, and one on a hidden sheet.
  - **Giving those a home (`attachment-home`, I.8; always on since D381).** They get one section at the end of the document, "Files attached to this document", made by code, numbered next if the document numbers its sections. Links and checks can then reach them, and a change to one changes that section's hash. A flat document's one section is their home instead, and no section is added. A document with such files gains text and a section, so its next paid run covers it again. No real PDF with an attached file has been through it.
  - **Hostile input, by stated limits (`ImportLimits`), refused and listed:** a zip name that climbs out, is absolute, has a drive letter or a control character, or runs over 200 characters; a symbolic link; more than 10,000 entries; one file over 256 MB; 1 GB in all for one attachment, across everything nested in it; over 500 times its packed size past 10 MB; more than 3 files deep. Bytes are counted as they unpack, not read from a header. Writing checks once more that each path stays inside its folder. Each `.docx`, `.xlsx` and main workbook is put through the same unpacking check before it's opened.
  - **Ids** keep the same bytes in the same section, then the same bytes anywhere if unique, then the same name in the same section, since an edited file is still that file. Anything else is new; an id nothing claims is marked removed and never comes back. A later file's attachments wait in its comparison, under `.stangent/fsd/<doc>/compare/attachments/pending-0001/`, and take their ids when `fsd take` takes their section, by the same bytes and then the same file name within that section (D396, below).
  - **The client-data check.** `stangent data check` flags a tracked `.xlsx`, `.docx` or `.pdf` anywhere, on purpose. An import no longer leaves any `original/` in the repo (I.7). What it can still flag: one from before this, and a file nested in an archive that was kept and not read, under another path.
  - **Attachments in the checks** went with the link route (D342). A frozen link quoting an attachment is still checked against that file by `trace check`.
  - **Known gaps:** only hand-built files, never one saved by Word or Excel. A hostile PDF can still end the process as it opens (the page tree above), and now one inside a document can too: it can't be guarded before opening. Macros are named from stream names only. An old `oleObject*.bin` with no package in it is kept, not read. A plain `.csv` or `.txt` inside a document is kept and not read, as the owner listed the formats to convert.
- **Fingerprint (D123, A.8).** Each section keeps `textHash` and gains `contentHash`, over its text and its attachments' hashes; with none it's equal to `textHash`. An older document has none: it still loads, and matching falls back on the text.
- **Section ids:** the FSD's own number (`4.2.3`), or the heading path (`Calendar / Views`) where a section has none.
  Duplicate, missing and skipped numbers are reported in `problems`, never guessed. In a PDF, a parent with no number (`Chapter 7: Title`) doesn't stop
  its children keeping theirs (`7.1`), and a number out of step with its parent (`8.3.2` under `8.2`) keeps its id with a problem.
- **Links hold them too (D121, `trace-links` v4):** a link's `fsd.sectionId` is its section's permanent id, beside `section`, the number. `trace check` fills and corrects it, even without `--fix`, from `document.json`, where a section's number meets its id (`ids.json` only lists ids). The number stays, because entry ids and saved hashes are built from it; no hash reads `sectionId`. `fsd take` carries it, and `carried.sectionIdsDisagree` lists an entry whose number and id named different sections. A marked-removed entry keeps the id its mark holds. A feature has `sectionId` too, from when it's built, or the next `req prepare`.
- **Permanent ids (D117, `source-document` v2):** each section also has `permanentId`, `S-0001`, from `specs/fsd/<doc>/ids.json`. It stays with the section across versions and is never given twice. `id` stays as it was, because saved input hashes and link ids are built from it. A document imported earlier gets its ids from `fsd migrate` (`permanentIdsAdded`); since phase fs layer 2 the id is the heading's marker in the chapter file (D382).

### An edit not committed yet (D131, D392)

Steps read the committed chapters, so `fsd status`, `fsd outline`, `fsd show` and `fsd export` warn on stderr when the working tree's chapters, `document.json`, `ids.json` or attachments differ from HEAD, and show HEAD's text; `status` and `outline` also carry `uncommitted: true` and `uncommittedFiles` in their result (replacing `documentEdited`).

### Before a commit: `stangent fsd check <doc>` (phase fs layer 3, D380, D382, D384, D393, D396)

An FSD edit goes: edit the chapter files, run `stangent fsd check <doc>`, read the list it prints, commit. The check is the
one command that reads the working tree's chapters; it compares them with HEAD's by permanent id. Code only.

- **What it writes:** each heading typed with no marker gets the next id from `ids.json`, added at the end of its heading
  line (`## 1.2 Returns <!-- S-0004 -->`); and a removed section's trace entries (below). Nothing at all when anything is
  refused. Run twice, the second gives nothing more.
- **A removed section's trace links (D396), as promote did:** when a committed section is gone (said with `--removed`, or
  its heading taken out with its marker), each `specs/trace.yaml` entry citing it moves to `(removed S-0004) 4` (its id
  and HEAD's number), so a new section given that number can't pick it up, with a new entry `id` (the hash of the link,
  an `id-moved` line in `specs/trace-history.jsonl`); its title, decision and `decided_by` stay. An entry cites it by its
  permanent id where it has one, else by its number; one whose number is the removed section's while its permanent id
  names another is left where it is and listed in `trace.sectionIdsDisagree` (D121); one marked by its permanent id while
  its number was another is listed there too (`markedBy: sectionId`, `removedNumber`). `trace.removed` lists each removed
  section with its entries and their decisions, and `uncommittedFiles` gains `specs/trace.yaml`: commit it with the
  chapters. Run again, nothing is marked twice. A removal undone before the commit (the heading and its marker back)
  puts its entries back on the number it has now (a new id where that number isn't HEAD's) (`trace.restored`); a committed removal can't come back
  (its id is refused, `old-id`).
- **Refused, with each problem under `result.refused` (`fsd_check_refused`):** a marker that doesn't read as one
  (`damaged-marker`) or sits on a line that isn't a heading (`marker-off-heading`); an id on two headings (`repeated-id`,
  a copied heading); an id `ids.json` never gave (`unknown-id`), marks removed (`removed-id`), or gave before HEAD with no
  committed heading holding it (`old-id`); an `ids.json` that went back (`ledger-went-back`); a section gone since HEAD
  while a heading has no marker (`marker-removed`: it may be that section retyped, so it is never guessed; the message
  names a heading that looks like it, by title or words, and the marker to put back, or says to run again with
  `--removed S-0007` if it goes on purpose); an unmarked heading that looks like a committed section whose marker now sits
  on a heading that doesn't (`marker-moved`); a heading whose id's committed section had other words and that now holds
  another committed section's words (or, failing words, its title), where that section no longer holds them itself
  (`marker-on-other-section`: a marker laid over another heading, two markers swapped, two "Overview" headings swapping;
  two sections sharing a placeholder such as "TBD." stay apart); a ruled section whose heading and words both changed
  (`rewritten-with-rulings`), unless `--same S-0002` says it is still that section; a heading more than one level below the one before it where HEAD's wasn't (`level-skip`; one the
  committed FSD, or a first import, already has is a warning); and a section removed while `rulings.yaml` (the working
  tree's or HEAD's) holds a person's work on it (`rulings-on-removed`, listing the entries), unless `--removed` names it:
  its entries then stay in `rulings.yaml` and are listed under `rulingsLeft`.
- **Warned (`result.warnings`):** a number on two headings (`repeated-number`) or lower than the sibling before it
  (`number-out-of-order`): numbers are the PM's text (D384); a level skip the committed FSD already has.
- **What the commit approves (`result.changes`):** every section `added`, `removed`, `renamed` (its number or title),
  `moved` (another chapter file, level or parent) or `changed` (its own words) against HEAD, by permanent id, so a rename
  or a move between chapters keeps its id and its rulings; `uncommittedFiles` lists the document's files that differ,
  new chapter files included (`git commit -a` would leave those out), with `ids.json`, `document.json`, `rulings.yaml` and
  attachments. Chapter files are taken as `.md` in any case, as HEAD's reader takes them. `status` is `ready`, `unchanged` or `refused`.

### Ranking doubtful matches (I.6, D131)

Each doubtful section in `fsd diff` also has `suggestions`: the unmatched sections on the other side, closest first, by the share of words they have in common, with the figure beside each. It only gives a person an order to read in. There's no cut-off, every section sharing a word is listed, and nothing reads it to settle a match: `fsd resolve` is still the only way.

### Where a document stands (D122, E.1)

`stangent fsd status <doc>`, or `/stangent:fsd-status`, by code, writing nothing: `current` (hash, title, sections, verdict), `unread` (why the committed FSD can't be read: not committed, not migrated, not git, `invalid_fsd`), `comparison` (a new file's: hash, file name, counts per case, `outOfDate`), `doubtful`, `coverage` (below), `queues` by who they wait on (`ba`, `unclear`, `agreeLinksToApprove`, `developer`, `developerConfirm`, `pageOwner`, with counts of `recheck`, `gone`, `closed`, `decided`), `reviewed` (whether `review.json` exists), `uncommitted` and `uncommittedFiles` (above), and `next`: who, what, and the exact command. Doubtful sections of a comparison come first, then a comparison with sections to take, because nothing else is worth deciding on words about to change; then the review (D341), the BA, developers, page owners and approvals. `next.who` is `nobody` when nothing waits.

### Reading a document (D122)

- `stangent fsd outline <doc> [--format text]`: each section's level, permanent id, number, title and size in words and lines, with `chapter` on each top-level heading, its place in the list.
- `stangent fsd show <doc> <section> [--format text]`: that section's text, by permanent id, number or heading path, with its `tables` and `figures` ids, and its `attachments` and generated `attachmentLines`.
- `stangent fsd export <doc> [--out <dir>] [--max-lines N] [--whole]` writes the chapter files as they are on disk, without their id markers (D378): with `--whole`, one file `<doc>.md` holding the whole FSD; otherwise one Markdown file per top-level heading, into `.stangent/fsd/<doc>/export/` by default. Git ignores that folder only where the repo's `.gitignore` lists it. `stangent init` adds the line, so a clone set up any other way shows `.stangent/` as untracked, and nothing adds it later. `ignoredByGit` in the result says whether the folder is ignored, for any folder. The limit is `fsd.exportMaxLines` in `stangent.yaml`, 1500 by default, a stated setting and not a measured one. A chapter over it is cut at its next heading level and so on down: `01-00-s0001-rules.md` holds the text before its first sub-heading, `01-01-s0002-pay.md` the first sub-heading. One section over it with no sub-heading stays whole, and is in `overLimit`. Text before the first heading is `00-front-matter.md`. Each file's first line says it's generated, is never read back and names the source's hash. Empty sections keep their heading. The same document gives the same bytes. An export removes only what an earlier export wrote, by that first line. A test holds that nothing in `src/` reads these files.

### A new file: compared, never imported over (D117, D385, D388; phase fs layer 5, D395, D396)

A first import goes straight in, and committing its chapters approves it (first-import approval was removed by D388). Until that commit the same file again is `unchanged`, and another file is refused with `fsd_not_committed`. Once the FSD is committed, `fsd import` of any file for that document id (the same one again too) changes no chapter: it writes a comparison to `.stangent/fsd/<doc>/compare/` (`comparison.json`, `comparison.md`, the new file's `import-report.md` and pictures), status `compared`. `fsd reread` does the same with the archived original read by this build.

- **The last file's sections.** `document.json` keeps `fileSections`: each section's number, title, heading path and text hash as the last imported file gave them, by permanent id, and `converter` (`format`, `version`). The import, `fsd migrate` and `fsd take` write them. A document written before layer 5 has none: its sections are then matched by the chapters of the commit that approved its import (the first commit after `importCommit` that touched them, in `base.from`), and nothing is offered from them, since that commit can hold the PM's own edits: each difference is `both-changed`, and `baseNote` says so. New sections can still be taken, and `take` records only what it took.
- **Matching, by code, in order, on the section model, so `.docx` and `.pdf` alike.** A heading path is the titles from the top down, without numbers. "Unique" means in the whole document, on both sides. First against the last file: the same heading path and text hash, then the same text hash (not empty); a section the repo took out is matched only by these, and reads `removed-here`. Then against the repo's text:

| Rule | Matches |
|---|---|
| the same body and heading path | as a unique pair; its number may change |
| the same body | unique and not empty |
| the same heading path | as the last file had it, then as the repo has it; unique |
| doubtful | anything left that shares a whole sentence with any section on the other side, listed with those candidates and marked `alreadyMatched` where they are; also anything left with the same heading path as a section left on the other side (`sameHeading: true`, no `sentence`) |
| new, gone | left, with no shared sentence and no such heading |

  A sentence is one from `Sentences.Of`, with whitespace normalised and table rows counted, holding at least 4 words. There's no score and no threshold. A split or a merge lands in doubtful, on both sides; a repo section whose sentences went to two or more file sections that share with no other also has `splitInto`, each part `splitFrom` (I.5). Suggestions rank the unmatched sections by shared words, for reading only (I.6).
- **The cases, per section** (`case` on each entry of `sections`, counted in `counts`). A section's heading is its number as written (without the " (2)" a repeated one's id gets) and its title.

| Case | When | What happens |
|---|---|---|
| `file-changed` | the file's heading, text or attachments differ from the last file's, and the repo's don't | offered: `fsd take` copies it in |
| `new` | in the file, matched to nothing | offered: `fsd take <doc> <its number in the file>` |
| `both-changed` | both differ from the last file's, and from each other; or the repo's section has no last-file entry | shown side by side in `comparison.md`; the repo's text is kept, and take refuses it |
| `repo-changed` | only the repo's differs | kept: the file is behind |
| `gone` | in the repo, not in the file (or settled `--removed`) | listed, never deleted |
| `removed-here` | the last file had it, the repo took it out, the file still has it | stays out |
| `doubtful` | see above | a person settles it first |
| `same` | the same heading, text and attachments on both sides | nothing; a move in the file (`fileMovedIt`) is listed |

  Renumbering counts: a section Word renumbered because one before it went is `file-changed` with `headingChanged` and no `textChanged`. **A section's attachments count as its words do (D396):** `fileSections` keeps each section's attachments as the file gave them (file name and hash), so a section whose words are the same and whose attachments the file changed is `file-changed` with `attachmentsChanged` (each file `added`, `changed` or `removed`, against the repo's) and no `textChanged`; one whose attachments the PM changed (a record in `document.json`, or a file under `attachments/` edited and committed: `attachmentFilesEditedHere`) reads `repo-changed`, or `both-changed` when the file changed it too (`attachmentsChangedHere`), and is never taken. An entry written before D396 has no attachments, so a section holding some reads as changed here until it is taken by hand. The comparison keeps the file's attachment files under `compare/attachments/pending-0001/` and on, in `fileAttachments`' order. The new file's problems are in `problems`, each on its section's permanent id where it has one. `converterChanged` says when the converter (or its version) differs from the last import's, or that the last one isn't recorded: a section the file seems to have changed may be the converter's doing.
- `stangent fsd diff <doc> [--format md]` shows the comparison. `no_comparison` (exit 3) when there is none.
- `stangent fsd resolve <doc> <file-section> --same-as <S-id>` or `--new` settles a doubtful section of the new file; `stangent fsd resolve <doc> <S-id> --removed` says a repo section is gone from the file (listed, never deleted). `--same-as` also settles the repo section it claims. Who decided comes from `git config user.name`, never an option; with none set it stops with `no_git_identity`. The move is `fsd-section-match` on the state module, keyed `<doc>@<file hash 12>#<section>`. Refusals: `not_doubtful`, `section_id_taken`, `section_id_removed` (a removed id never comes back), `not_found`, `comparison_out_of_date`. The same file compared again keeps what was settled.
- `stangent fsd take <doc> <section>… | --all-updates` copies the file's heading and text into the chapter files, uncommitted. `--all-updates` takes every `file-changed` section; a new one is named by its number in the file. Refused per section, listed in `notTaken` with why: anything not `file-changed` or `new`; a section whose working-tree text or heading differs from HEAD's (an uncommitted edit is never overwritten); one the file put at another heading level; a new one whose preceding section in the file isn't in the repo. A new section goes after the section it follows in the file, at the file's level, and gets the next id from `ids.json`. Before writing, every other section must read as before (title, level, text, parent) and each taken one as the file has it, or nothing is written (`take_out_of_step`); nothing taken is `nothing_taken`. A number used twice is warned (`numbersRepeated`), as `fsd check` warns (D384): usually a section gone from the file still holds it here. `comparison_out_of_date` when HEAD's chapters or `document.json` moved since the comparison. Each taken section's `fileSections` entry moves to the file's, with `original`, `converter` and `importCommit`; the file's problems on the sections taken replace the last file's there; tables get their ids again; pictures the text names are copied in. **Its attachments come with it (D396):** each of the file's attachments on the section keeps the `A-` id of the one the repo holds there with the same bytes, then with the same file name; any other gets the next id from `ids.json`; one the repo holds there that the file no longer has is marked removed in `ids.json` (rule 11, never given again) and its folder goes (as it came, it's in the archive). The files under `attachments/<id>/` and the records in `document.json` become the file's (the record's `sectionNumber` is the section's number here); other sections' are left alone, and a section whose only change is its attachments keeps its words byte for byte. Before anything is written, a section is refused (in `notTaken`) when its attachment records or files differ from HEAD's (an uncommitted edit is kept), or the comparison's copy of one isn't what the file gave (import it again). `result.attachments` lists, per section, the ids `kept`, `added` and `removed`, and `changed` names the folders. An attachment that moved to another section keeps no id across sections: the one it left is marked removed when that section is taken, and it gets a new id where it lands. Files attached to the whole document (no section) aren't taken: they're listed under `attachmentsWithNoSection` and stay as the last import brought them. Trace entries follow a section whose number moved (below). Then `fsd check` and a commit: the commit approves it (D380), and the next file compares against what was taken.
- **Removed by D388 (layer 5):** `fsd promote`, `fsd discard`, `fsd approve`, `fsd reject`, `fsd import --partial`, `candidate/`, `SECTION_DIFF.md`, `promotions.jsonl`, `sections/`, and the `fsd-candidate` and `fsd-section-answer` lifecycles (their old lines in `specs/fsd/history.jsonl` stay, unread).
- **What follows a section, by code, with no file changing shape.** When `take` moves a section's number or heading path, trace entries get the new number and title, and a new `id` because it's the hash of the link, with the decision and `decided_by` kept. `carried.traceIds` holds old and new entry ids; `sectionIdsDisagree` lists an entry whose number and permanent id name different sections. Nothing else is carried since the link route was cut (D342).
- **A modified section** then goes through the existing drop-back (D58) once committed: `trace check --fix` drops back what lost its quote, and `staleQuotes` lists it.
- **A removed section** (since D396, `fsd check` marks it, above) is listed with its trace entries. Nothing is dropped. Its trace entries move to the section `(removed S-0007) 3.2`, so a new section given that number can't pick them up, and a new `id` because it's the hash of the link, in `trace.traceIds`. Their title, decision and `decided_by` stay, and the new id gets an `id-moved` line (below); `trace.removed[].decisions` lists what they hold. `trace check` flags them until a person deals with them, and its fix leaves them alone, since there's no section to compare a quote with. A screen whose only link is on a removed section counts as undocumented, a draft leaves that section out, and impact answers and stale-page lists show the mark.
- History: a moved entry's new id gets an `id-moved` line in `specs/trace-history.jsonl`, so `state history` shows what happened under the old id. Its `X-` id never changes.
- CI: `TraceCheck` and `trace attribute` look up a moved entry in the base file by its link without the section, so a take or a removal doesn't read as a typed `decided_by` or a new decision.
- **Known gaps:** see `phases/2b-fsd.md`. No real document with two versions has been through this yet (D56).

- The original goes to the archive set in `stangent.yaml` (`fsd: { archive: { name, path, git } }`, a folder outside
  the repo or a second git repo, which gets a commit) and is cited by archive name, path and content hash.
  A file with a known hash is imported once.

## The FSD review (layer 5, D341; contract `fsd-review` v2, matches D366)

The route measured better than the old linking (D321–D324: 26–29 of 30 planted items against 24, 0–8% false alarms
against 31%): an agent reads the FSD and the source, two runs side by side, and code checks what it says. The old route,
`/stangent:fsd-link`, was cut in layer 7 (D342). `plugin/scripts/fsd-review-run.sh <doc>` (or
`/stangent:fsd-review`) runs it all; by hand:

```bash
stangent fsd review <doc> --run 1        # writes .stangent/fsd/<doc>/review/run-1/input.md, names stangent-fsd-reviewer and its limits
stangent fsd review-check <doc> --run 1 --usage <claude result>
stangent fsd review <doc> --run 2 …      # fsd.reviewRuns, default 2
stangent fsd review-merge <doc>          # → specs/fsd/<doc>/review.json and review.md
```

- **Input** (`fsd review`): the reviewer's instructions (D321's prompt, with no app named), `fsd.scope`'s route
  prefixes, the current version's sections as Markdown with each id in its heading (`### 2.1 Library  [section id:
  2.1]`), and the output schema `fsd-review-output.v2.json`: findings with `state` (gap, conflict, undocumented, and
  since D366 match: a statement the code does as written, with the line that does it),
  `section`, `fsdQuote`, `screen`, `note`, `evidence` (`path:line`). A checked run with the same input on the same commit
  is `current`, no AI; `--again` runs it anyway. AI off (`ai.enabled: false`) refuses.
- **Limits**, as the file finder's (D101): Read, Grep and Glob in a throwaway worktree of the commit the run was prepared on; never `.stangent/`,
  `.git/`, `specs/` (earlier reviews, the FSD's record) or `stangent.yaml`; structured output; `fsd.reviewBudget`
  (60 turns, $3 per run by default, the retry inside it). Kavita's runs used 205–242K tokens.
- **Checks** (`fsd review-check`), every finding: a known state; a match gives at least one `path:line`, and one run
  can't report the same section and quote as a match and as a gap or conflict (the match goes back for the retry, the difference stands, D366, D367); the section exists in the reviewed version (null only for
  an undocumented screen that names its route); a gap or conflict quotes its section word for word, 4 words or a whole
  line, from a word's start to its end, with spaces and `*`/`` ` `` ignored, curly quotes and long dashes folded, and
  table bars and list markers ignored where that makes it match, a section holding its subsections' text; every evidence path is a
  file at the reviewed commit and its lines inside it (a path alone may be a folder). Any failure: one retry with every
  error (`feedback.md`, `previous-output.json`; a schema error names its field). After it, failing findings are dropped
  and listed with their reasons in `checked.json`, never shown as findings. No answer after the retry: the run failed,
  nothing is recorded, and the next `fsd review` starts it again. A second check while the retry waits, with no new answer
  and no `--usage`, is refused (`no_output`). The run's stage (prepared, retry, checked, failed) moves through the state
  module (`fsd-review-run`); `stangent fsd review-status <doc>` shows each run's stage and commit and reads only.
- **Merge** (`fsd review-merge`): every run checked, on one commit and FSD version, or it refuses. Joined, one finding
  per run at most: a gap or conflict with the same section and quote; an undocumented screen with no section and the same
  route; an undocumented finding in a section only when the pair is unambiguous (same section, routes equal or one blank,
  an evidence file in common, and each the other's only such finding). Anything else stays apart; undocumented findings of
  different runs in one section sharing a file are marked `maybeSameAs`, for the reader. A joined finding keeps the first
  run's words and every run's evidence, and lists its `runs`. A match joins another run's match by section and quote; where another run calls that statement a
  gap or conflict, both stay and each lists the other in `disagreesWith` (the difference is what gets ruled). `review.json` holds the commit, FSD version, model, effort,
  tokens, each run's counts and usage, the findings (`F-001` …, in section order, not permanent) and what was dropped;
  `review.md` is the same by section (gaps, conflicts, left out, matches), then screens no section covers. Both are generated:
  answers don't go here: the PM rules on the findings through the truth map (below: `truth-map.md`'s numbered questions, `fsd rule`; the old route's `trace.yaml` decisions stay with it).
- **What it doesn't show:** that a difference is real. The quote and line checks keep the AI honest about what it read;
  D322's checker found 0 of 20 false after the quote rule, on one app.
- **Checked free (2026-11-15):** the saved Kavita runs through the built command: 29 of 30, 0 dropped
  (`benchmark/results/2026-11-15-fsd-review-free/`).

## The truth map (D361–D374; `phases/tm-truth-map.md`), being built

The PM's rulings on the review's findings judge tickets. Built so far:

- **Links** (`stangent fsd links <doc>`, D362, layer 1; reads only): each finding of `review.json` links to the most specific
  thing that fits, per evidence line: a **flow** (the line is a step's, or inside a method a flow walks; a start flow before
  a shared one), else a **screen** (an inventory line, or a file holding inventory lines: the screen, shared part or App frame
  group with the most lines there; an undocumented screen naming its route links to that screen), else the smallest graph
  **node** holding the line, else the **file** (or folder). The finding takes its best line's link. Each link carries a
  fingerprint of what it points at (the flow's file, the screen's lines, the node's own lines, the file), so a ruling is
  flagged when that changes. Printed: counts per kind and the share linked by fallback (node or file), e.g. "51 findings:
  flow 12, screen 35, node 4, file 0 (fallback 7.8%)" on Kavita's saved review (2026-11-16, D368). Within a kind, an exact step or control beats a walked method or a nearby one; a file holding several groups goes to the nearest inventory line. Needs `specs/flows/`,
  `specs/inventory.json` (v2, with the App frame) and the graph; a missing one is a warning, and nothing links to it.
- **Rulings** (`stangent fsd rulings <doc>`, D363, D365, D366, layer 2; code only): drafts or refreshes
  `specs/fsd/<doc>/rulings.yaml` (contract `rulings` v2 since phase fs layer 4, D386; a v1 file is still read) from `review.json`. Needs the FSD imported (rulings are kept by
  section). One block per FSD section with findings, then `undocumented:` for code no section covers. Each entry has a
  **key**: a gap, conflict or match by its section's permanent id and its quote (normalized as the merge joins quotes, so
  spacing, emphasis and curly quotes don't matter); undocumented code by its link (D362), so the findings in one section
  sharing a link are one entry with all their notes and lines (D365 (2)). A statement one run called a match and another a
  difference is one entry, the difference, marked `disagrees`. Within a section: differences in the order their quotes
  appear, undocumented code by link, then matches; the order is stable, so a refresh moves no line. Each match has a
  **confidence** set by code (D366 (4)): high when every run found it and it links to a flow or a screen, medium for one of
  those, low for neither. The PM edits only `ruling` (gap or conflict: `code-right`, `fsd-right`, `restated`; undocumented:
  `keep`, `remove`, `restated`; match: `confirmed`, `not-a-match`), `statement` (required for `restated`), `pmNote` and a
  section's `matches: confirmed` (all its matches at once), and deletes a `flagged:` or `new:` line once they've looked.
  On a refresh: the same key keeps its ruling; a new finding gets a blank entry, and a new match in a section whose
  matches are confirmed gets `new: true`; an old entry no run finds again stays, marked `notFound: <review commit>`, when
  it holds a person's work (a ruling, statement, note, or a confirmed match), and is dropped and listed when blank (a
  `notFound` entry stays until a person deletes it); a section whose own text changed drops its rulings to blank with
  `flagged: "FSD section changed since ruled (was …)"` and clears its `matches`, and so does an entry whose quote (which
  may come from a subsection) is no longer word for word in the section's text ("its FSD quote is no longer in the
  section"); an edit in a subsection alone leaves the parent's other rulings as they are. Either way a `restated`
  statement the new text holds word for word is simply a match (D386): the entry becomes a confirmed match on it, with
  `writtenFrom`, as `fsd write` would have made it (counted `writtenByHand`); a ruled entry whose link's fingerprint changed gets
  `flagged: "link changed since ruled"`; a ruling of the wrong kind after the review changed its state (a conflict now a
  match) is blanked and flagged. A file that needs fixing (not YAML, schema errors, an entry with no key, two entries with one
  key) is refused, and nothing is written. The PM's `ruling` and `matches` lines are written back as typed (quotes, a
  comment), and the file's line endings are kept, so `git blame` keeps naming them; other comments aren't kept. Text
  holding a colon or a `#` goes in quotes; a file that isn't YAML is refused with its line. Of two equally good links for
  one entry, the first by kind and id wins, so a re-review that reorders findings flags nothing.
- **The check and the truth map** (`stangent fsd rulings-check <doc>`, D363, D366 (3); code only): every value is one of
  its state's, `restated` has a statement, `matches` is `confirmed` or blank, every entry has a key and sits under its own
  section. Problems are listed and nothing is written. Otherwise it writes `specs/fsd/<doc>/truth-map.json` (contract
  `truth-map` v2 since phase fs layer 4, for agents) and `truth-map.md` (the same, for the PM), the same bytes from the same inputs. **Who ruled
  and when** come from `git blame` of the working file on the entry's `ruling` line, or for a match its section confirmed,
  on the `matches` line: the commit's author and date; a line not yet committed shows `committed: false` and a warning
  (commit, then run it again). Each entry gets a **standing**: `not-found` and `flagged` first, then `blank` or `ruled`
  (differences, undocumented), `confirmed`, `unconfirmed` or `not-a-match` (matches). Printed: counts by standing and state,
  unconfirmed matches by confidence, per section, the links per kind and the fallback share ("42 entries: flow 11, screen
  27, node 4, file 0 (fallback 9.5%)"), `verifyFirst` (unconfirmed low, then medium matches, for the PM at setup), and
  `done` when every entry is ruled, confirmed or not a match. `truth-map.json` holds every FSD section in order with its
  text and its entries (ruling, statement, who, standing, link, evidence, runs, confidence), the code no section covers,
  and each linked flow's actor, steps with their conditions, scenarios, endings and its 🟡/❓ counts. `truth-map.md`
  shows the matches to verify first, then each section's text as a quote with its entries, then the flows (30 steps and
  20 scenarios each, then "and N more, in <file>"). A ruling made before the commit reads `not committed`.
- **The PM answers in plain words** (layer 2b, D371; code only): nobody edits `rulings.yaml`. `truth-map.md` opens with
  how to answer and the numbered questions still open (medium and low matches first, then items to look at again, then
  the unanswered, then sections whose matches aren't confirmed); each section's entries are numbered, with what the
  review found in plain words ("The FSD and the code disagree") and, while open, the question and its answers ("**Which is
  right?** `code` · `fsd` · or the rule in your own words"). A section's matches question comes before its entries; the
  code no section covers is numbered last. `truth-map.json` holds each entry's `n` and each section's `matches.n` (null
  with no matches). The PM tells Claude "12 yes, 13 the FSD is right" (`/stangent:rule <doc>`, `plugin/commands/rule.md`),
  and Claude runs **`stangent fsd rule <doc> <n> [answer] [--words "…"] [--note "…"]`**: `yes`/`no` for a match or a
  section's matches, `code`/`fsd` for a difference, `keep`/`remove` for undocumented code, `--words` for the rule in the
  PM's words (restated, or with any answer), `clear` to undo, `drop` for an item the review didn't find again. The number
  goes back to its key through `truth-map.json` (the map the PM read), and the key is looked up in today's
  `rulings.yaml`: an item a refresh removed or changed in kind is refused, never put on another. An answer that doesn't
  fit its item, or would leave the file one `rulings-check` refuses (a match `restated`), is refused, and nothing is
  written; so is a file with problems, and a `--map <id>` other than the one at the top of `truth-map.md` ("Questions
  numbered for map `…`": a map rebuilt since numbers differently). The file is written as `fsd rulings` writes it: on a
  file stangent wrote only the answered lines change; on a hand-edited one the PM's `ruling` and `matches` lines are kept
  as typed, but `statement` and `pmNote` come back quoted and comments go, which blame doesn't read. Answering with the
  answer already there changes no line, so blame keeps naming who gave it first. `--note` replaces an earlier note (the
  read-back says so); answering clears the item's `flagged` and `new` marks
  (looked at). The map isn't rebuilt, so a number keeps its meaning until `fsd rulings-check` runs; nothing is committed,
  and the commit's author is who answered. Kavita: numbers 1–42, the answers wrote only their lines (and, once, the
  header's new wording).
- **Rulings become the FSD's words** (phase fs layer 4, D379, D386, D387; `stangent fsd write <doc> [--max-cost N|none]
  [--section <id> [--item <n> --map <id>]]`): every ruling that becomes words, not written yet, is turned into a chapter
  edit in the working tree, uncommitted; a person runs `fsd check`, reads the chapter diff and commits (D380). The PM's
  own words (`restated`) are placed by code: for a gap or conflict they replace the quoted sentence, found word for word
  (spacing, curly quotes and `*`/`` ` `` marks aside) in the entry's section or one under it; for code no section covers
  they are added as a paragraph at the end of the section `--section` names (by id or number; `--item`, the truth-map
  number, when more than one waits), and without `--section` they are listed, never guessed. A ruling that the code is
  right goes to an AI step (D60): with `--max-cost` (dollars, or `none` for the drafter's own $2) `fsd write` writes the
  input of `stangent-fsd-writer` (each entry W1, W2, … with the sentence to replace, its section's text, the review's
  words and the linked code), the driver `plugin/scripts/fsd-write-run.sh --max-cost N <doc>` runs it read-only in a
  worktree of HEAD under the cap, and **`stangent fsd write-check <doc>`** checks each draft by code: `replaces` is the
  ruled sentence word for word; the new words are one line, not empty, not the old sentence, hold no heading, no
  `<!--`/`-->` and no line break; at least one cite, each a real `path:line` or `path:from-to` at the run's commit and
  inside the entry's linked code (within one of its evidence ranges, or every line of it linking to the entry's own flow,
  screen, node or file). Passing drafts are written; one retry for the failed and missing ones, then they are listed.
  Without `--max-cost` those rulings are listed with the command to run (rule 9). **Only the ruled words change:** the
  edit replaces the located span and nothing else, and is refused (listed) when the quote is found more than once, or
  not at all; when bold or code marks run past it (marks right around it go with it); when it runs across paragraphs,
  list items or table rows; when the words would change the layout (a line break, a heading, `<!--`/`-->`, a list,
  quote, table or rule start, a code fence, a bold or code mark left open, an HTML tag, a `|` in a table row); when the
  quote sits inside words the same run wrote; when the chapter file mixes CRLF and LF; and when the section it lands in doesn't read as the
  text its rulings were recorded on (its block's `textHash`, else HEAD's text): an uncommitted hand edit, or a newer FSD
  that `fsd rulings` hasn't seen. After the edit, every other section must read byte for byte as before, with no heading
  gained or lost, and the rulings as they would be written must pass every reader's check, the schema included. A ruling
  drafted from a Jira reply is written only once committed (D374), checked again at `write-check`; a flagged ruling, or one the
  last review didn't find again, is listed. **Each written entry becomes a confirmed match** (D386): keyed by its section
  and the new sentence, same link, evidence and notes, `ruling: confirmed`, and `writtenFrom: { state, section, quote |
  link, ruling, statement, cites, by, on }` (who ruled, from blame when the ruling was committed before it was written).
  Undocumented code moves into the section it was written into. The section's `textHash` moves to the new text, so a
  refresh doesn't flag the edit; a refresh never marks a written entry not found, and drops the review's finding it
  replaced (`supersededByWritten`) while the old sentence is gone, so it doesn't come back blank. `rulings-check` lists written entries
  under `written` (ruled by, committed by, the Jira author), and `truth-map.md` says "Written into the FSD from the PM's
  own words by Pat PM on …, written in a commit by Dana Lead on …; it replaced: “…”". A committed ruling not yet written
  is a decision on file and judges tickets meanwhile. `/stangent:rule` runs `fsd write` after the answers, asks the PM
  for a section or a cost cap when one is needed, then `fsd check`, and reads the chapter diff back before offering the
  commit.
- **The verdict** (layer 4, D372; always on since D381): a ticket bundle's `truth` lists the entries on the
  ticket's files; `ticket prove` stops with no test when no agreed rule can back the ticket (unclear) or one says the
  code is right (contradicted), checked by code, and the questions draft ends with the PM's question. Details in
  `docs/tickets.md`.
- **`Ruling:` replies from Jira** (layer 5, D364, D374): `ticket pull` drafts a reply to the PM's question
  into `rulings.yaml`, uncommitted, with `jira: { key, comment, author, created }`; details under Pull in `docs/tickets.md`.
  A drafted ruling backs no ticket until it is committed. A ticket nothing covered gets an undocumented entry of its own,
  keyed `ticket:<KEY>` on the ticket's code, which a refresh keeps and never marks not found. `rulings-check` lists every
  ruling from Jira under `fromJira` (the Jira author, the git committer, `differ` when they aren't the same name, case
  aside) and warns while one isn't committed; `truth-map.md` shows "in Jira by Pat PM (SHOP-1, comment 101), confirmed by
  Dana Lead on …", or "committed on …" when they are one person, and a ticket's own entry as "added from Jira SHOP-1, not a
  review finding".
- **Email templates in the graph** (layer 3, D362 (5), D370; `code-graph` v7, `docs/code-graph.md` "Template files"): a finding on
  a template file links by its `template-file` point (a node link, so still a fallback), and the graph's 🟡
  `sends-template` line from the constant naming it (`EmailConfirmTemplate`) lets a ticket starting in the sending code reach
  the template's rulings (layer 4). Kavita: 22 templates linked; none of the saved review's findings cites one, so its
  links are unchanged.
- **Checked free on Kavita (2026-11-17):** the saved review (51 findings) gives 42 entries in 19 sections and 1
  undocumented (20 undocumented after grouping by link), all blank; flow 11, screen 27, node 4, file 0; a second
  refresh carries all 42, and rebuilds are byte for byte the same. The saved runs have no matches, so confidence, `new`
  and section confirmation are tested with fakes only. Fresh-checked (D369).

## Links, findings and decisions (2b, D53, D58, D62): frozen (D342)

**The link route is cut (D342).** The candidate finder, link judge, agree checker, disprover, requirement steps, claims and
claim links, workflow files, test journeys, `spec ask` and the security flag went in layer 7, with their commands
(`fsd candidates`, `fsd prepare`/`check`, `fsd disprove …`, `fsd claims`, `fsd undocumented`, `fsd not-in-code`, `workflows`,
`req …`, `ticket match`, `spec ask`, `trace confirm`, `send-back`, `security`), agents, schemas and script modes. The FSD
review (above) is how an FSD is checked against the code now; git history keeps the old code. A `stangent.yaml` that still names
one with a cut `fsd:` or `spec:` key fails to load; since D381 a `components:` key is ignored with a warning, whatever it names.

**What stays.** `specs/trace.yaml` stays as the old runs left it, because ticket starting points and the ticket bundle still read
its links and findings, as they read the reference pages (D342: cut those too once a ticket run without them shows the points
hold). Table facts (below) still write their findings into it. So:

- **`stangent trace check [--base <ref>] [--fix]`**, the CI check: schema, ids, both quotes still in the FSD and the page (a frozen
  claim link is checked word for word against its section's text, the claim ledger having gone), the statement's text still what the
  decision was made on (otherwise the decision drops back to undecided), cited symbols, `decided_by` never typed. Findings get their
  permanent `X-` ids, evidence (`evidence` v1) and severities as before (D118); old verdicts and security flags on file are read and
  shown, never written.
- **`stangent trace approve <doc> <chapter>`** sets `agree` on every confirmed agree link under a chapter;
  **`stangent trace attribute --base <ref> --by <who>`** fills `decided_by` and `severity_by` after a merge. People still edit only
  `decision` and `severity`, in a PR; every move goes in `specs/trace-history.jsonl`.
- **`stangent trace links <doc>`** and **`stangent trace findings <doc>`** read the file: each link with its page statement and the code
  lines it cites, and the BA's queue as Markdown. Reads only.
- **`fsd take`** carries the entries to a section's new number by permanent id (D385).
- **`fsd status`** still reports each section's coverage by links, from the frozen file.

## Comparing tables with the code (C.2, D124)

**A second route to code, on since D381.** Everything else reaches code through pages. D68 kept direct links out as noisy, and that question is the owner's and still open, so this is labelled on every result. **It hasn't been shown to help, and there's no real FSD on a C# project to pilot it on:** every test uses hand-built documents.

- **`stangent fsd tables <doc>`** lists each table with its `T-` id, each attachment's sheet with its `A-` id and sheet name, their columns and rows, and any attachment with no sheet to read. Code only, so a person can write the map first.
- **`specs/fsd/<doc>/table-map.yaml`** (contract `table-map` v1), which a person writes or approves. An AI may propose one later; not now. `aliases` is where a name difference is declared, and nowhere else.

  ```yaml
  version: 1
  tables:
    - { table: T-0001, column: Field, kind: api-field, target: POST /api/orders response }
    - { table: T-0002, column: Status, kind: enum-member, target: OrderStatus, valueColumn: Code }
    - { table: A-0001, sheet: Fees, column: Tier, kind: enum-member, target: FeeTier }
  aliases:
    - { fsd: Customer Name, code: customerName }
  ```

- **Facts from the code.** `api-field`: the JSON fields the graph holds for a type an endpoint sends or receives, one level deep, by the type's name or graph id, or by `<VERB> <route> request` or `response`. A match says how many `maps-field` lines reach it as `frontendFields`. `enum-member`: from `.stangent/graph-enums.json` (`docs/code-graph.md`). **Not done:** constants and SQL seed data, which the result says under `notDone`.
- **`stangent fsd compare-tables <doc>`** applies the map, by code with no AI. A name matches in any case and nothing else, so `customer_name` against `customerName` needs an alias. For each line it gives `matches`, `differs`, `fsdOnly`, `codeOnly` and `skippedRows`. `differs` needs a value to compare, so today it's only an enum's `valueColumn`: the graph holds no value for an API field. `unmapped` lists every table no line names, and `attachmentsNotRead` every attachment with no sheet, so nothing is silently unchecked. A line it can't apply goes in `problems` and leaves what's on file from it as it was. With no map it says `noMap` and writes nothing.
- **Each difference is a finding of its own kind, `table-fact`,** in `specs/trace.yaml`, with `by: code` and what was compared in `table_fact`: `route: table-facts`, `result` (`differs`, `fsd-only`, `code-only`), the table, column, row, target and both values, and the code's `path`, `start`, `end` and `commit`. Its quote is the table's cell, or the column's header for a name only the code has. Its evidence is that cell, `stated` as every document citation is, and the code's lines, `read`; `table_fact.strength` is `read`. A file that may not match the commit gets `linesNot` and no lines.
- **Where it goes.** The BA's queue, with state and history like other findings, labelled by its route. `compare-tables` writes it. It has no page, so it doesn't enter a link bundle, a ticket bundle, a draft or an impact answer, and doesn't count a section as linked. It isn't sent to the disprove step, because code already compared it. A judge's run doesn't touch it. Running `compare-tables` again replaces its own, and a decided one that went away is kept as `gone`.


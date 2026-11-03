# The spec (Phase 2: contracts `reference-page`, `source-document`, `trace-links`)

"The spec" is three layers (D52), each with its own check:

| Layer | Where | Written by | Checked by |
|---|---|---|---|
| **Reference pages**: what the code does, per screen, shared component and backend entry point | `specs/reference/` | AI (Claude Code subagents, D60) | code: schema, every citation in the graph and in the page's slice; a fresh verifier (D62) |
| **The FSD**: the documents the client agreed, converted to text | `specs/fsd/<doc>/` | the client / BA; converted by code | same file → same bytes |
| **Links, findings, decisions** | `specs/trace.yaml` | AI proposes, people decide | code: quotes word for word, ids, cited symbols, `decided_by` from git |

Everything above is committed: AI output is stored, not regenerated (D18). The graph (D25) and the AI steps' working
files (`.stangent/spec/`, `.stangent/fsd/`) are not.

## How an AI step runs (D60, D61)

Every AI step is the same small loop. The CLI holds all the logic; a driver only runs the subagent each step names.

1. `stangent … prepare` writes the step's input as one file (instructions, output schema, the evidence) and prints
   `result.next`: `{action, agent, prompt, output}`, or `action: none` when nothing needs AI (inputs unchanged, D18).
2. The driver starts the named subagent from the plugin with exactly that prompt. Its tools are `Read` and `Write`.
3. `stangent … check [--usage <claude json>]` checks the output by code and prints the next step. A failed check
   gives the subagent **one retry** with the specific errors; a second failure is recorded as failed.

Drivers: `/stangent:spec-generate` and `/stangent:fsd-link` in a Claude Code session, and
`plugin/scripts/spec-run.sh` for batches and CI (headless `claude -p --output-format json`, so tokens, cost, model and
time are recorded per step; `--jobs N` runs pages side by side). The batch driver makes **one turn per call** (D66):
the step's input files go into the prompt, Claude Code runs with no tools and none of the user's settings, skills or
MCP servers (`--tools "" --setting-sources "" --disable-slash-commands --strict-mcp-config`), the output schema is
enforced with `--json-schema` (`next.schema`; `stangent spec schema <name>` prints it), and the driver writes the
structured answer to `next.output`, so `check` is unchanged and input hashes are the same in both modes. `--agents`
runs the plugin's subagents instead, each allowed to read and write only under `.stangent/`
(`--permission-mode dontAsk --allowedTools "Read(.stangent/**)" "Edit(.stangent/**)"`). Files
drivers share are written whole (temp file, then rename) under a lock. A section whose candidate pages have no recorded
version is reported as failed, never turned into a gap. `ai.enabled: false` stops every `prepare` with exit
code 4 (D24); `spec stale`, `fsd import`, `fsd candidates`, `fsd undocumented` and `trace check` are code only.

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
  and `sideEffects`; every statement cites symbol ids. Code numbers them S1…, A1…, R1…, E1….
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

## The FSD (2b, D13, D16)

- `stangent fsd import <file> [--id <doc>] [--flat] [--accept-changes]` takes `.docx` or `.pdf` (D111), by code only: the same file gives the same bytes, and
  what can't be brought in exactly is listed in `problems`, never dropped. `.xlsx` is decided but not built: `unsupported_format`.
- **`.docx`** converts with the Open XML SDK, in C#: headings (Word's automatic numbering worked out, numbers typed into headings, or
  plain), numbered and bulleted lists, tables, images with their captions. Inline formatting is dropped, because quotes are matched
  against this text. The same principle as a PDF: what can't be brought in exactly is listed in `problems`, by kind and section (D116).
  - **Tracked changes** (inserts, deletes, moves, including in a footnote, on a paragraph mark or a table row) stop the import: `docx_tracked_changes`, with a count, nothing written. The document isn't settled. Format changes don't count: they change no words. `--accept-changes` imports the accepted view: inserted text kept, deleted text gone, moved text once, a deleted paragraph mark joining the two paragraphs, as in Word. `problems` says how many were accepted. It's refused for a PDF. A re-import of a file already recorded stays `unchanged`, with or without the flag.
  - **Hidden text** (`w:vanish`, on a run or through a style, by Word's rule that a character style and a paragraph style cancel) is left out and listed with its word count, and so is a hidden table row. A hidden paragraph mark alone hides no words, only the line break, so its visible text stays, as its own paragraph, and is listed.
  - **Struck-through text** stays, listed, since a strike can mean a retired rule and only a person can tell.
  - **Comments** stay out of the text. Each is one `problems` line in a fixed shape, `comment: section "4.2" by "Jane" open: "Is it 3 or 5?"` (`DocxComment.Parse` reads it back), with its state from Word's own resolved flag, in the order of their anchors. One with no anchor left has section `(no anchor)`.
  - **Footnotes and endnotes** come in at the end of their section, as `[Footnote 1, after "the last five words"] text`, numbered in reading order, which can differ from the number Word prints. The sentence itself gets no mark, because quotes are matched against it.
  - **Other things a file can hold:** a text box keeps its text where it's anchored, once, listed. SmartArt keeps its words as a list, listed, since its order isn't always the drawing's; in a table it's only listed. Embedded files (by program id, such as `Excel.Sheet.12`), charts, equations, symbols, ActiveX controls, old-style and linked pictures and `altChunk` documents aren't read, and are named with their section. A content control keeps its content, but a prompt still showing ("Click here to enter text") is left out and listed. A field keeps the text Word last saved, listed unless it's only a link, page number or contents. Page headers and footers aren't imported: one line with their word count.
  - **Tables:** a nested table keeps its words and is listed as flattened. A cell merged down rows repeats its value in each row, and is listed; text Word doesn't show in such a cell is left out and listed. A cell merged across columns keeps its value in the first column and blanks after it. A row that starts part-way across the grid keeps its cells under the right columns, and rows and cells inside a content control are read.
  - **Completeness check (D18), on every import:** the check reads the file apart from the converter, by element name in any namespace, every word in a text-bearing element. Per section, those words, minus what a rule removed, plus what the converter adds on purpose (numbers, list marks, table rules, footnote labels, repeated cells), must equal the words rendered in `document.md`. A mismatch adds a line, `completeness: section 4.2: 3 words in the file aren't in document.md ("…")`, and never fails the import, so a kind of element nobody thought of can't vanish silently. A word it can't see, such as text inside a picture, it can't check.
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
  between `--flat` and not replaces the document. Any re-import that replaces a document reports `replacedSections` in the command's result: the section ids that left, and how many `specs/trace.yaml` entries point at them, because `trace check --fix` clears those decisions.
- **Errors, with nothing written:** `no_structure` (a PDF with none of the above, or a `.docx` with no headings), `no_text` (no page has text), `pdf_locked` (any encryption, even one that opens without a password; protection is never worked around), `invalid_pdf` (PdfPig couldn't read the file, including an outline that loops or nests impossibly deep; usually damage, but it can be a valid file PdfPig can't handle), `invalid_docx` (damaged, or an encrypted Word file), `docx_tracked_changes` (a Word file with tracked changes and no `--accept-changes`).
- **Known Word gaps:** never yet run on a file saved by Word itself, only on hand-built files and Sonarr's planted FSD, which a script wrote. A tracked merge of table cells (`w:cellMerge`) counts as a change but isn't applied. White text is kept and not listed. A style from a table doesn't count for hidden or struck text. A hidden run that splits a word leaves its two halves joined. A file imported before this check stays `unchanged`, with no new lines, until its bytes change. Ticket attachments read the accepted view with nothing listed, because a ticket bundle has no place for it.
- **Known PDF gaps:** a table split across pages stays as separate pieces; images are unread; no OCR, so a page with no text is listed and skipped; a title on a page doesn't prove it's the heading there. Fully borderless tables can't be detected without guessing: they flatten into text, unlisted. Tables ruled only across flatten into text too, and are listed; underlined lines close together are listed the same way, as a false alarm. Two columns drawn row by row can interleave. A word can split where PdfPig reads a gap in it. A capitalised sentence over a page break stays split, and a footnote at body size can join a lower-case next page (the join is listed). Near-white and fully transparent text is kept and not listed. Text covered by a shape isn't detected. Table rows drawn apart from each other lose their columns but keep their words in order. Text in another direction is listed with a page count, not its page numbers. Rotated column heads leave a table's header cells blank: their text is listed as another direction. A list item's label can read as a sentence's start, or the reverse, where nothing else marks a list. A page tree nested about 100,000 levels deep overflows PdfPig's stack as the file opens and ends the process (nothing is written): it can't be guarded before opening, and it takes a hostile file.
- **Section ids:** the FSD's own number (`4.2.3`), or the heading path (`Calendar / Views`) where a section has none.
  Duplicate, missing and skipped numbers are reported in `problems`, never guessed. In a PDF, a parent with no number (`Chapter 7: Title`) doesn't stop
  its children keeping theirs (`7.1`), and a number out of step with its parent (`8.3.2` under `8.2`) keeps its id with a problem.
- The original goes to the archive set in `stangent.yaml` (`fsd: { archive: { name, path, git } }`, a folder outside
  the repo or a second git repo, which gets a commit) and is cited by archive name, path and content hash.
  A file with a known hash is imported once.

## Links, findings and decisions (2b, D53, D58, D62)

- `stangent fsd candidates`: candidate pages per section, by code: section titles ↔ routes, component names and page
  titles (case, word order and plurals normalised), field labels in tables ↔ properties in a page's slice, and the
  endpoints a candidate screen calls. A section with none goes to `stangent-candidate-finder`, which sees the page
  titles only.
- `stangent fsd prepare|check <doc> <section>`: `stangent-link-judge` (fresh context) returns links — `agree`,
  `conflict`, `gap`, `undocumented` — each quoting the FSD sentence and the page statement exactly. Code checks both
  quotes word for word, both ends exist, and every symbol the quoted statement cites is in the graph. Every `agree`
  then goes to `stangent-agree-checker`, a second fresh check: `confirmed`, or `disputed` and turned into the finding it
  raises. `specs/trace-runs.json` records each section's inputs, candidates (by code or AI), tokens and retries.
- `stangent fsd undocumented`: screens no section links to are undocumented, by code, within `fsd.scope`: the areas
  the FSD says it covers, as routes or route prefixes (`/settings` covers `/settings/tags`). It only sees screens with
  a recorded page, so generate every in-scope screen first.
- **Findings are off by default (D68):** with `fsd: { findings: false }` (the default) sections record only links;
  gaps, conflicts and undocumented items aren't written and `fsd undocumented` lists none. `findings: true` turns them
  on (the Sonarr benchmark does); changing it makes each section run again.
- **`specs/trace.yaml`**: one entry per link or finding; people edit only `decision` (`agree`, `code-wrong`,
  `fsd-wrong`, `defer`) in a PR. `stangent trace approve <doc> <chapter>` sets `agree` on every confirmed agree link
  under a chapter in one edit. `stangent trace check [--base <ref>] [--fix]` is the CI check: schema, ids, both quotes
  still in the FSD and the page (otherwise the decision drops back to undecided), cited symbols, and `decided_by` never
  typed. After a merge, `stangent trace attribute --base <ref> --by <who>` fills `decided_by` from the code host. Every decision's move goes in `specs/trace-history.jsonl`.
- Impact answers reach FSD sections through the links (`impact-report` v5, `docs/code-graph.md`).

## Requirements (2c, D99; contract `requirements` v1, being built)

The FSD stays the record. On top of it, one AI pass per section builds items, each quoting the FSD word for word. Status starts
`proposed`; only a person approves.

- `stangent req prepare|check <doc> <section>`, the usual loop. `spec-run.sh --req <doc> [--only <section>]` runs it in batches.
  `stangent req sections` lists sections with text; `stangent req show <id>` prints one item, never a whole file.
- **`stangent-requirement-writer`** returns requirements (kind, title, statement, quotes, acceptance criteria), definitions (glossary,
  data, roles, states, messages, outside systems, scope, success criteria, only when the FSD says something about them), journeys and
  scenarios made of requirement keys, questions for the BA, and background.
- **Code checks, one retry:** the schema; keys unique and pointing at something; no statement twice; every quote from the section word
  for word; every sentence cited or background. **Cited** means the quotes of requirements, criteria and definitions cover every word of it,
  whole words only (a piece under 4 words counts only as a whole sentence), and a table row must be quoted whole. **Background** is one sentence
  exactly as listed. A second failure fails the section; `--retry-failed` tries again.
- **`stangent-requirement-checker`** (fresh context, sees the section, the items, the background and the questions) says whether each item goes further than
  its quotes, naming the words (code checks they're in the item), whether each item cites a quote it has nothing to do with, and whether each background sentence holds
  a rule. Disputed ones go back to the writer once, then to the checker again; what's still disputed is recorded `disputed`. A dispute it couldn't back up and then dropped
  leaves the item `unchecked`.
- **Files,** committed, under `specs/requirements/`: `ids.json` (the permanent-id ledger: an id is given once), `history.jsonl` (every status move, `docs/foundation.md`), `runs.json` (cost, retries, counts), and per document
  `F-<id>.yaml` (one per section, following the FSD's section tree, parents listing children), `questions.yaml`, and two views written by code: `index.yaml` (one line per
  item, read first) and `glossary.yaml`. Working files are in `.stangent/req/`.
- **Ids** are plain numbers, assigned once: `F-`, `R-`, `D-` (definitions), `J-`, `S-`, `Q-`. On a re-run an item with the same quotes and words keeps its id, as does one reworded
  item alone on its quotes; ids whose quotes are gone are retired. A change to the title, statement, kind, quotes, criteria or the definitions it uses, or any answer from the checker but `ok`, drops an approved item to
  `proposed`. Reconcile (not built) replaces this with carrying items forward by code.
- **Not built yet:** approval, links, reconcile across documents and renumbering, a pass for clashes between sections, Jira and as-built sources, diagrams. Journeys are within one
  section, since extraction sees one section at a time.

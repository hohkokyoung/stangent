Short entries for what's still in force. Full text, and everything archived: [archive/5-spec-and-finder.md](archive/5-spec-and-finder.md).

# Decisions D99–D111: the spec on the FSD, the file finder and closing Phase 3

## D99 · The FSD gets a checked layer: word-for-word quotes, stable ids, people approve — 2026-11-02
Nothing rewrites the imported FSD. Everything built on it cites word-for-word FSD quotes that code checks. Ids are plain numbers, given once, never reused; section and title are fields, not part of the id. No AI resolves an ambiguity: it goes to the BA. Unapproved text is context only and never decides "bug or by design". An approved rule outranks a ticket: a fix against it stops and asks. A bug report never creates spec text; a developer confirming a bug says if it shows a spec gap (default no). A stale page's links are judged again; `/stangent:adopt` replaces D14's audit. **Why:** only a person's approval makes spec text true. **Cost:** the BA's time. Still planned, not built: several documents (FSD, change request, addendum), each imported on its own with the effective date and status (draft or approved) it states; a change request touches only what it mentions; Jira change-request tickets as sources; when an import lands, CI runs `fsd reconcile` and opens a spec-only PR for the BA. AI-written requirements, their judge and ticket match to be replaced by D156 P4/P5. FSD journeys are built by code (D160).

## D100 · Held-out bugs come from bitwarden/server; Sonarr's 40 are the tuning set — 2026-11-02
Changes are tried on Sonarr's 40 bugs. The held-out set is 24 bitwarden/server bugs (`benchmark/tickets/bitwarden/heldout.json`), frozen before any run, pulled through their own Jira project, and measured only to judge a finished change. No proof runs on bitwarden, so proof bars are Sonarr's alone, and reports say so. **Why:** a measure tuning has seen can't tell a gain from fitting; a second repo checks stangent isn't shaped around Sonarr. **Cost:** a smaller set with wider ranges, and its own setup.

## D101 · A coding agent finds the ticket's files; code checks them; the graph is the checker and the map — 2026-11-02
A read-only agent, given only the ticket, searches a throwaway worktree of the commit and names files. Code checks each: tracked at HEAD, a word-for-word ticket quote, a word-for-word file line (not an import). One retry; a file still failing is listed, not dropped. The graph checks and expands what it named. With the finder off, code's signals are the starting points. Answers stay while the ticket is unchanged, `stale` when code moves. **Why:** right place 36/40 on Sonarr, 20/24 held out. **Cost:** an AI call per ticket. **Uncertain:** D156 P6 puts code's signals first, after Phase 3's numbers are checked.

## D102 · A ticket Jira sends as HTML reads as the same Markdown as any other — 2026-11-02
The pull turns Jira's HTML into the Markdown Jira would have sent: links, images, numbered and task lists, code marks and blocks, quotes, headings, tables; media stays `[attachment: name]`. Our own code, no library, no backslash escapes. The quote check ignores a line's Markdown marks. **Why:** a ticket should read the same whichever format Jira picked; links and code marks are leads. **Cost:** HTML tickets' hashes changed once, so their finder answers ran again.

## D103 · A code quote may be one whole line, even with no space — 2026-11-02
A code quote still needs 12 characters. A piece of a line needs two words; a quote with no space counts when it is a whole line after trimming. The finder's instructions keep "two words" until their next real change, since they're in its input hash. **Why:** a whole line is as specific as two words; the rule stops fragments, not statements. **Cost:** none measured.

## D104 · The proof's turn cap counts model rounds, what `--max-turns` limits — 2026-11-02
The driver counts rounds (one per model message id in the transcript), not `num_turns`, which parallel tool calls inflate. Claude Code's own stop, the rounds and the dollar cap bound an attempt; the 30 stands. With no `rounds`, only Claude Code's stop and the dollars apply. **Why:** the old count threw out 6 finished answers. **Cost:** attempts use more of their turns; the count is checked on saved transcripts, not by `dotnet test`.

## D105 · The on-fix check says what kind of failure it was; D79's bar stays — 2026-11-02
`--verify` sets `verification.result` by code: `passed`, `new-failures`, `same-failure` (the proof's `shows` words still appear), `different-failure`, `not-compiled` (saying if the error is in the test) or `not-run`, plus `testChanged` and `othersChecked`. D79's bar counts only `passed` on the answer key's fix commit; the rest are reported beside it. No AI judges the failure. **Why:** a bare "failed" can't be acted on. **Cost:** code says what happened, not why; a pass can still be for the wrong reason.

## D106 · "Not reproduced" stops saying "don't fix" and says why — 2026-11-02
Changes D75. A failed attempt records `noProof`: `ran-out`, `test-passed` or `flaky`. Phase 3 says "no proof yet, the developer decides". Phase 4's agent still waits for a person's go when there's no failing test. A questions draft covers the attempt only if a test ran; code refuses a claim linked to a `ran-out` proof. **Why:** "don't fix" held back real bugs that had only hit the cap. **Cost:** a developer may fix a wrong report with no warning.

## D107 · The finder's reason is a guess: labelled for people, hidden from the proof — 2026-11-02
The finder's one-line `why` is unchecked. It is left out of the reproducer's input, shown in `bundle.md` as "The finder's guess, not checked:", and kept in `bundle.json`. Root cause isn't Phase 3's job; Phase 4's green test is the evidence. **Why:** a test should check what the ticket reports, not a guess. **Cost:** the reproducer could still open `bundle.md`.

## D108 · `ticket post` refuses a draft whose inputs changed — 2026-11-02
`post` recomputes the draft's input hash (ticket, bundle, proof, instructions). Changed, or impossible to work out, gives `draft_stale` unless `--stale`; the error gives the command to make it again. Pass fixed commits, not a range like `HEAD~1..HEAD`. **Why:** a person reads a draft but can't see its facts moved. **Cost:** posting loads the graph and pages; an instructions change makes every saved draft stale.

## D109 · Phase 3 closes on what we have; the proof ships as advice only — 2026-11-02
Phase 3 closed with two bars failed and its proof numbers older than the finder, D95, D104, D106 and D107; reports say so. The proof re-run (Sonarr, about $45 at most) is owed before Phase 4 is built, on the owner's OK. **Why:** what was left needed Phase 4 or moved no bar. **Cost:** a person makes every bug-or-not call. The order of work replaced by D112 and D156.

## D110 · The core stays C#; no port to Python — 2026-11-03
Roslyn must stay C#, so Python would mean three runtimes. Measured speed and a self-contained build showed no need, and no failing bar traces to the language. A new product may still use its own language through the CLI (D9, D23). Reopen only if a measured failure traces to the language, as a port-only step with identical hashes as its bar. **Why:** a port costs re-checking, and saved AI outputs' hashes are built from C#-made text. **Cost:** core work needs .NET; a non-developer install is a large build.

## D111 · Stangent imports PDF and Excel as well as Word — 2026-11-03
The same rules as Word: code only, no AI, the same file gives the same text, the original is archived, and what can't come in exactly is listed. PDF is read directly: sections from bookmarks, else a printed contents page checked entry by entry, else stop (`--flat` gives one labelled section). Headings are never guessed from fonts. Only ruled grids become tables, every cell checked word for word, else plain text, listed. Excel follows D123. Details and gaps: `docs/spec.md`. **Why:** the owner's documents come in these formats. **Cost:** a PDF's import is weaker than Word's; a new dependency.

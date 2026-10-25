---
phase: 3
name: tickets
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3, impact-report: 5, reference-page: 1, source-document: 1, trace-links: 1 }
provides: { ticket-bundle: 1, ticket-update: 1, bug-proof: 1 }
---

# Phase 3 · Tickets

**Approved 2026-10-25 (grilled as D69–D81). Step 0, the MCP spike, passed 2026-10-25 (Progress; D82).**

A Jira ticket becomes a checked context bundle for the developer's Claude Code (D69): the ticket and its attachments,
where in the code it lands, the reference pages and FSD sections for those places, and the impact answer. A bug is
proven by a failing test before anyone fixes it (D75). Comments back to the ticket are drafted by AI and posted by a
person (D74).

Every step follows D18 and D60: code does what it can; each AI step gets one bundle file, returns JSON in a schema,
and is checked by code before anything uses it.

## What waits on other phases
- **Nothing waits on 1d.** Bundles start from pages and the impact answer, which exist. If 1d part 2 changes the slice
  rule (D45), pages go stale through their input hash and bundles flag them (D71).
- **Phase 5 overlaps:** the bug proof writes and runs unit and Playwright tests (D75, D76). Phase 5 later chooses
  tests from the graph, adds integration tests and tags tests with requirement ids; it reuses Phase 3's test runner
  and `tickets.app` setting rather than building its own.
- Phase 4 reads `ticket-bundle` v1 unchanged (D69).

## Where the files live
```
.stangent/tickets/<KEY>/   ticket.json, attachments/, raw MCP results, points, bundle.json + bundle.md,
                           AI step inputs and outputs, drafts, the proof record   (git-ignored)
<the repo's test projects> the reproduction test, on the developer's fix branch, committed with the fix
```
*Plan detail, not a numbered decision:* bundles hold client text and are rebuilt from Jira and the graph, so they stay
out of git. The AI outputs in them (picks, drafts, the proof record) are kept locally with their input hashes, so a
re-run with unchanged inputs makes no AI call (D18). What lasts goes elsewhere: the posted comment is in Jira; the
reproduction test is "kept with the ticket" (D75) as a file in the repo's test project on the fix branch, recorded by
path in the proof record, and committed in the same PR as the fix.

## What gets built

### 0. The spike, first (D73)
From C#, with the official MCP SDK, against the owner's site (`peachpersonal.atlassian.net`): sign in with OAuth and
keep the refresh token in the OS keychain; read a ticket with comments and an attachment; download the attachment;
post a comment; then the same with an API token (Basic auth). **If any part fails, stop and take it back to the owner**
before building on it.

### 1. The issue-tracker adapter (code, D24, D73)
- **Jira backend:** the Atlassian MCP's tools, called from code. Code reads only the fields it names; each raw result
  is saved with its hash. A change in Atlassian's output fails an adapter test instead of changing a bundle quietly.
- **File backend:** tickets from JSON files. Used by tests (no network) and to **replay** saved MCP results in the
  benchmark (D77).
- Settings in `stangent.yaml` (`tickets:`; names are plan details): site, project keys, which issue types are bugs, which fields hold steps /
  expected / actual / version / environment, the default tone (D74), the bundle cap (D71), `tickets.app` (D76) and
  `tickets.proveBudget`, the cap per reproduction attempt (D81).

### 2. Pull (code)
- `stangent ticket pull <KEY>`: the ticket (summary, description, comments, links, type, status), converted to Markdown
  by code; every attachment and inline image downloaded with its hash (D72).
- Text attachments go in up to a per-file cap, and stack traces in them feed the starting points; `.docx` through 2b's
  converter; images, PDF, Excel, video and anything else are listed.
- For a bug: steps, expected, actual, version and environment pulled into their own fields; missing ones are noted
  for the questions draft (D74, D75).

### 3. Starting points (D70)
- **Code:** stack-trace frames (in the ticket or a text attachment) → methods (✅); URL paths → route pages; quoted screen names, page titles and FSD section
  titles → pages (sections through `specs/trace.yaml`); symbol names found in the graph. Each point records its signal.
- **AI, only when code finds none:** `stangent ticket prepare <KEY> --step points` writes the ticket plus the list of
  page titles (not the pages); `stangent-point-picker` returns up to 5 pages, each with a word-for-word ticket quote;
  `stangent ticket check` checks pages exist and quotes match; one retry.
- **The developer:** `stangent ticket points <KEY> --add <page|symbol> --remove <page|symbol>`; changes are recorded.
- Setting `tickets.aiPoints` (default on; off if D79's bundle bars fail).

### 4. The bundle (code, D71)
- `stangent ticket bundle <KEY> [--format md]`: the ticket, the starting points with reasons, the chosen pages
  (statements with citations), their linked FSD sections' text and link states, the impact answer, and pointers to
  files and symbols. No source code.
- **The impact answer** starts from the chosen pages' cited symbols and any matched symbols, with `--max-depth 2`
  (D27; D28's "within 2 lines"), summary first.
- **Cap** ~40k characters (`tickets.bundleChars`): over it, the lowest-ranked pages become pointers and the bundle says
  so. Missing and stale pages are flagged, not generated; the developer can run `/stangent:spec-generate` for them.
  Findings only when `fsd.findings` is on, marked unconfirmed.
- Same inputs → byte-identical bundle. Validated against `schemas/ticket-bundle.v1.json`.

### 5. The bug proof (D75, D76)
- `stangent ticket prove <KEY>` prepares the input: the bundle, the repo's test projects for the code at the starting
  points, the ticket's expected and actual result.
- **`stangent-bug-reproducer`,** the project's first AI step that writes code and runs commands. It may edit only
  files under the repo's test projects (or a new Playwright spec folder) and run only the build and test commands
  stangent hands it, enforced with permission rules as D60 does (*plan detail:* D60's rules so far only cover
  `.stangent/**`). It returns the test's path, the assertion that checks the expected result, and the ticket sentence
  giving that expected result.
- **Code checks:** the test compiles; it **fails on an assertion** (not a compile error, a setup exception or a
  timeout); the failing assertion is the one named; the quoted expected-result sentence is in the ticket word for word.
  Whether the assertion really checks that sentence is for the developer (D75). A Playwright test must fail the same
  way on 3 runs in a row. Unit first; Playwright only when no unit test can reach the behaviour and `tickets.app` is set.
- **Outcomes,** recorded as `bug-proof` v1: reproduced (the developer confirms the failure matches the reported actual
  result); not reproduced (the test passes: no fix starts, a questions draft says what was tried; the developer reviews
  the test first); not reproduced, flaky (a Playwright test that passed on one of its 3 runs, D76); not provable here
  (no way to reach it); or failed (*plan detail:* no valid test after the one retry).
- **"Don't fix yet"** after "not reproduced" is a label and a suggested next step, not a lock (D75). The developer can
  ask for another attempt with a hint: `stangent ticket prove <KEY> --hint "set the server time zone to UTC+10"`,
  checked the same way and recorded with its hint (~$0.50–2 per attempt).
- Later, the fix must make the test pass and break nothing else (D18's test row). *Plan detail:* Phase 3 checks that
  with `stangent ticket prove <KEY> --verify` on the fix branch.

### 6. Drafts and posting (D74)
- `stangent ticket draft <KEY> --kind questions|resolution [--tone internal|client]`:
  - **questions** from the bundle: unclear points and clashes with the FSD or a page, each quoting both sentences word
    for word (checked as D53); missing bug fields; a not-reproduced proof.
  - **resolution** from the developer's merged PR: code supplies the diff's impact answer, the pages and FSD sections
    it reaches, and the proof's outcome; the AI words it.
- **Code checks** every claim links to a page, FSD section, file, PR or ticket sentence; a `client` draft must not
  contain code names or file paths. Validated against `schemas/ticket-update.v1.json`.
- `stangent ticket post <KEY> <draft>`: posts it under the developer's own login. Nothing else is written: no
  transitions, no field edits.

### 7. Plugin
- `/stangent:ticket <KEY>`: pull → points → bundle, shown to the developer; offers `prove` for bugs and `draft`.
- Subagents: `stangent-point-picker`, `stangent-bug-reproducer`, `stangent-question-writer`, `stangent-summary-writer`.
  Batch and benchmark runs use the single-turn driver where the step allows it (D66); the reproducer is multi-turn,
  since it builds and runs tests.

### 8. Proving it on Sonarr (D77–D79, D81)
- **Selection (code):** from Sonarr's main line before the pinned commit, newest first: 40 bug issues each closed by one
  fix commit of at most 15 files (`Closes #N`), labelled backend / frontend / both; 10 issues closed as not a bug, by
  design or can't reproduce, with no fix (D78). The list and each issue's text and attachments are frozen and committed
  in `benchmark/tickets/sonarr/` before the first run; the answer key (fix files, fix commit) never enters a bundle.
- **Into Jira (asked first):** the 50 copied into a new project on the owner's site, each noting its issue number.
  stangent reads them through the MCP; the raw results are saved and the scoring replays them (D77).
- **Sonarr's app setting** (`tickets.app`, *plan detail*): build and start Sonarr with an empty data folder and a seed
  step that adds series and episodes without calling its online metadata service, so Playwright tests run offline.
- **How the run is set up** (fixed before any run, D79 clarified): product defaults (AI picks on, `fsd.findings` off,
  the cap), no developer edits to the starting points, no `--hint` retries. Starting points are picked for all 50
  first; the picked pages with no written version are then generated (~50–100 pages, ~$10–20, reported apart). "Starting points' files" are the files of the symbols the
  starting pages' statements cite plus the files of matched symbols.
- **Proof runs** (D81) on the 30 newest bugs and the 10 not-bugs, each attempt capped at $1 and 15 turns (hitting the
  cap = not reproduced). They check out each bug's fix parent; "proof is real" re-runs the test on the fix commit. The 10 not-bugs
  have no fix commit: they run on the commit before the issue was closed and are scored only on "catches wrong reports".
  With no developer in the run, a test that passes the code checks counts as reproduced; "proof is real" measures what
  the developer's confirmation would catch.
- **Drafts:** a questions draft for all 50 and a resolution draft for each of the 40 bugs (from its fix commit, standing
  in for the merged PR), so "grounded" covers drafts.
- **The keyword baseline (code):** ticket words vs file paths and symbol names, top 5 files.
- **Scoring:** `--score-tickets` in `benchmark/Stangent.Benchmark`, with D79's bars, 95% ranges from 1,000 resamples
  (seed 42).
- **Cost before the run (D81):** worst case ~$40 for reproduction (40 attempts × $1 cap), ~$5 for bundles and drafts,
  ~$10–20 for missing pages: about $55–65.

## Done when (D79's bars)
- [x] **Spike:** OAuth and API-token sign-in, read, attachment download and posting work from C# (D73, D82).
- [ ] **Bundle: right place:** ≥ 70% of the 40 bugs have a file the fix changed among the starting points' files.
- [ ] **Bundle: beats a keyword search:** that hit rate beats the baseline, the 95% range of the gain above zero.
- [ ] **Grounded:** every starting point, quote and draft claim passes the code checks; tests show a made-up quote and
      a non-existent page are rejected.
- [ ] **Repeatable:** replaying saved MCP results gives byte-identical bundles; a re-run with nothing changed makes
      zero AI calls.
- [ ] **Proof is real:** of the bugs marked reproduced, ≥ 90% pass on the fix commit.
- [ ] **Proof finds bugs:** ≥ 50% of the 30 bugs the proof runs on reproduced (unit and Playwright reported apart, D81).
- [ ] **Proof catches wrong reports:** at most 1 of the 10 not-bugs reproduced.
- [ ] **Jira round trip** live on the owner's site: read a ticket with comments and an attachment, post a comment.
- [ ] Reported: how much of each fix the whole bundle covers; bundle size; cost and time per ticket and step.
- [ ] If a bar fails, its rule applies (D79): "proof is real" or "catches wrong reports" → the proof is advice only;
      "right place" or "beats a keyword search" → AI picks off by default; "proof finds bugs" → "not reproduced" is
      information only, not "don't fix yet". The report says which and why.
- [ ] Tests run without an LLM or a network (canned subagent outputs, the file backend); CI green.
- [ ] End-of-phase report written; its claims checked against the results files by a fresh agent (D61).

## Contracts
| Contract | v1 holds |
|---|---|
| `ticket-bundle` | ticket key, source and raw-result hashes; the ticket's text fields and attachments (hash, kind, text or pointer); starting points with their signal or quote and who added or removed them; pages, FSD sections and link states; the impact answer; pointers; cap and what was cut; missing and stale pages |
| `ticket-update` | a draft or posted comment: kind, tone, text, each claim and what it links to, the inputs' hashes, model and tokens, who posted it and when |
| `bug-proof` | ticket key, test path and kind (unit / Playwright), the named assertion, outcome, the commit it ran on, runs and their results, who confirmed it, and the fix-branch verification |

## Not in this phase
- Status transitions and field edits (D74); "code-wrong" decisions → tickets (D80).
- Change requests (features) scored in the benchmark (D78). *Plan detail:* the commands still build their bundles; they
  have no proof step.
- Integration tests with databases or containers (Phase 5); test selection from the graph and requirement tags (Phase 5).
- AI descriptions of images (D72); converters for PDF, Excel and video until a real ticket needs one.
- Scheduled or CI pulls of tickets: the API-token sign-in is proven by the spike, but no unattended workflow is built.
- Jira Data Center; issue trackers other than Jira (the adapter leaves room, D24).
- A dry run on one of the owner's real projects: after the Sonarr proof, checked by a person (as D56).
- The dashboard (Phase 8).

## Progress

### Step 0 · the MCP spike (D73) · 2026-10-25 · passed
From C# (`ModelContextProtocol.Core` 2.3.0) against `peachpersonal.atlassian.net`, on a test ticket created for it with
the owner's approval (SCRUM-7: one text attachment, a setup comment):
| Check | OAuth (browser) | API token (Basic) |
|---|---|---|
| Sign in; secret in the macOS Keychain; a new process signs in silently | ✅ | ✅ |
| Read the ticket (summary, type, description, status), its comment and its attachment's metadata | ✅ | ✅ |
| Download the attachment (`downloadJiraIssueAttachment` → short-lived URL → code) | ✅ byte-identical | ✅ byte-identical |
| Post a comment | ✅ comment 10001 | ✅ comment 10002 |
- **The endpoint is MCP v2, not v1** (D82): v1's 32 tools have no attachment download. Recorded as D82.
- Built: `src/Stangent.Tickets` (`Jira/JiraConnection.cs`, `Secrets/SecretStore.cs`) and `stangent jira login | logout |
  tools | call | download` (`src/Stangent.Cli/JiraCommands.cs`); these become the adapter's Jira backend (step 1).
- Problems hit and fixed: the SDK's 60 s initialization timeout cut off the browser sign-in; the callback listened on
  IPv6 only; the first keychain write leaked the v2 tokens into the session (D82's incident note; grant revoked).
- **Left to confirm:** the OAuth grant in use was approved with the server's full scope list (before the Jira-only
  filter existed). A sign-in with only D82's scopes is still to be run before Phase 3 is done. The API token was created
  with the Jira scopes only and passed.

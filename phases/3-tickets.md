---
phase: 3
name: tickets
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3, impact-report: 5, reference-page: 1, source-document: 1, trace-links: 1 }
provides: { ticket-bundle: 1, ticket-update: 1, bug-proof: 1 }
---

# Phase 3 · Tickets

**Approved 2026-10-25 (grilled as D69–D81). Step 0, the MCP spike, passed 2026-10-25 (D82); steps 1–7 built (Progress). Step 8, the Sonarr run, waits for the owner's go-ahead.**

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
  page titles (not the pages; since 2026-10-28 the D87 ranking, top 20 in detail plus every page numbered); `stangent-point-picker` returns up to 5 pages, each with a word-for-word ticket quote;
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

### Steps 1–7 · built · 2026-10-25
Code in `src/Stangent.Tickets`, commands `stangent ticket pull | points | prepare | check | bundle | prove | draft | post`,
the plugin's `/stangent:ticket` and `plugin/scripts/ticket-run.sh`, docs in `docs/tickets.md`. Contracts:
`ticket-bundle` v1, `bug-proof` v1, `ticket-update` v1; AI-step schemas `point-picker-output`, `bug-reproducer-output`,
`draft-output` v1. Tests need no LLM and no network (canned subagent outputs, replayed MCP results, a fake test runner).
- **Step 1:** `IIssueTracker` with the Jira backend (MCP v2) and a replay backend over saved results; the parser reads
  named fields only and fails on a changed shape; SCRUM-7's real output is the test fixture (personal details replaced).
- **Step 2:** a live pull of SCRUM-7, then a replay of what it saved, gave byte-identical `ticket.json` (checked live).
  A ticket's `source` is the tracker it lives in, also when replayed, so replayed bundles match live ones.
- **Step 3:** code signals first; the point picker only when code finds none; a made-up quote and a non-existent page
  are rejected (tested); an unchanged ticket makes no AI call (tested).
- **Step 4:** the impact answer starts from several points through `ImpactAnalyzer.AnalyzeStarts` (plan detail): the
  `impact-report` contract and its consumers are unchanged; the bundle's contract carries the answer.
- **Step 5:** checked once by hand on a real xUnit project: an `Assert.Equal` failure is found at its line; a
  `NullReferenceException` isn't counted as proof. *Plan detail:* the reproducer's turn cap is passed as `--max-turns`
  (not in this Claude Code's `--help`; unverified until a real run) and also enforced by `check` from the result's
  `num_turns`; the dollar cap is `--max-budget-usd`.
- **Step 6:** posting records who and when; a draft is never posted twice without `--again`.
- **Left open:** a sign-in with only D82's Jira scopes, to confirm they're enough (the spike's grant had the full list).

### Fresh review of steps 1–7 (D61) · 2026-10-25
A separate agent with a fresh context reviewed `7b5526f..95dfe40` against this plan and D18, D60, D69–D82. All 13
findings were checked against the code and fixed; none was rejected. Each has a test that fails without the fix.
| # | Finding | Fix |
|---|---|---|
| 1 | A Jest/Vitest package (or test project) at the repo root could never accept a test file | `.` targets accept any relative path without `..`; only a leading `./` is dropped |
| 2 | The $1 / 15-turn cap applied per call, so a retry got a fresh one (D81) | Turns and cost summed over the attempt; a retry gets what's left (`next.limits` on the retry) |
| 3 | Interactively, the reproducer ran without the permission rules or caps | `/stangent:ticket` runs it through `ticket-run.sh`; `check --step prove` requires `--usage` |
| 4 | An API token went to whatever `tickets.endpoint` a repo named | Only https Atlassian hosts are accepted, for both sign-ins |
| 5 | Unmarked sentences and a false proof claim passed a draft's checks | Every sentence that states something needs a claim; a proof link names the outcome, checked |
| 6 | The AI's "not provable" was final with no check, though code found targets | One retry first; if repeated, recorded labelled as the reproducer's judgment |
| 7 | Playwright's own retries could turn a flaky test into "reproduced" | `--retries=0` |
| 8 | Test names went unescaped into regex and VSTest filters | Escaped (`-t` anchored at the end) |
| 9 | Name matching by substring (`…_100` matched `…_1000`) | Exact full name, parameter list or method name |
| 10 | A moved issue's files went under one key and were looked up under another | Files stay under the key asked for; Jira's key is recorded as `jiraKey` |
| 11 | Attachment ids from a replay folder could escape the ticket folder | Ids must be numeric |
| 12 | Attachment downloads followed redirects anywhere | Redirects followed by hand, Atlassian hosts only |
| 13 | `--verify` broke after a newer `--hint`; an app that didn't start left no record | The attempt keeps its target; app failures are recorded as `failed` |

### Step 8 · preparation (code only, no AI) · 2026-10-26
- **The frozen set** (`benchmark/tickets/sonarr/`, chosen by `--select-tickets`'s rule, D78): 40 bugs, newest first on
  Sonarr's main line before 0bffefe7, each labelled `bug` and closed by exactly one main-line commit of ≤ 15 files (29
  backend, 10 frontend, 1 both); 10 not-bugs filed with the bug template, closed as not planned, referenced by no commit,
  not closed by a bot, with a maintainer's reply. `answer-key.json` (fix commits and files) is read by the scoring only.
- *Plan detail, decided before any run:* each issue is frozen with only the comments from before its fix (or closing)
  **and before the first maintainer comment**. A first version kept comments up to the closing, which put the
  maintainer's verdict ("that's by design") into not-bugs' text; it was caught on reading the list and re-frozen.
  39 comments kept, none after its cutoff (checked).
- **Scoring** `--score-tickets` (D79 with D81: proof bars on the 30 newest bugs); **Jira copy** `--copy-to-jira`
  (resumable; the GitHub number as a label `gh-<n>`, not in the text); **run** `benchmark/tickets/run.sh` in D79's and D84's order
  (points → pages → bundles → prove → drafts → replay; drafts after prove, D84).
- **Sonarr for Playwright** (`benchmark/tickets/sonarr/app/`): built at the checked-out commit, started offline with a
  fresh data folder, no sign-in, two series seeded straight into the database (no metadata service); checked: the UI
  loads, the API lists both series, and a Playwright spec run as the proof runs it passes.
- New settings (plan detail): `tickets.dotnetArgs` (Sonarr builds a single test project only with `SolutionDir` set),
  `tickets.testFilter` (the repo's unit-test filter for whole-project runs), `tickets.app.playwrightDir`.
- **Not-bug #9004** ("Backport Trakt show-link fix to Sonarr v4") is a real bug fixed in v5, closed only as not
  backported, so at the v4 commit it may reproduce. The owner's call (2026-10-26): keep it, since the rule picked it and
  removing it after reading the list would be hand-picking; if it reproduces, the report says why.


### Step 8 · the page index (D87) · 2026-10-27 · built and measured, not wired in yet
- **Built** (`src/Stangent.Tickets/PageIndex.cs`, code only): for every page its titles, its routes and endpoints, the
  names of the code in its slice, its written statements, its linked FSD sections, and the text its code holds: string
  literals (C#, TS/JS), markup text (TSX/JSX, HTML, Angular templates, Razor views by convention), `.resx` values the
  code names (`Resources.Key`) or look up by string (`localizer["Key"]`; the neutral file, else the English one), and the
  texts of the project's own text files for strings that are their keys. Text
  deeper in a slice counts less. Blazor `.razor` components aren't read: the graph has no points for them yet.
  New setting `tickets.textFiles` (plan detail; D87 said "a setting"): one list of files, no call names, because a key
  string is a key wherever it's written. Sonarr: `en.json`, which also covers `GetLocalizedString("Key")` (306 uses)
  and `Label = "Key"` attributes.
- **Ranking:** each ticket word counts by its rarity across pages × its best weight in the page; a run of 2+ ticket words
  in the same order inside one piece of a page's text adds a phrase score; a phrase never crosses punctuation (`:` `,`
  `(`, a full stop and a space). Template lines are dropped (markdown headings, ticked boxes, the keys of
  `- Key: value` lines). Scores are scaled down a little for big pages. A **clear winner** shares a phrase and scores
  1.5× the runner-up; the next 4 are "also possibly". Every page is ranked; ties by id. 0.4 s to build on 718 pages;
  ~9 ms a ticket.
- **Tuned on Sonarr, then frozen** (D87, agreed): depth discount 0.35, phrase boost 2, winner 1.5×, summary words 2×,
  size scaling 0.1, chosen from a 54-setting grid by hits. Tried and dropped, since they gained nothing: matching a ticket
  word to longer words it starts (`qbit` → `qbittorrent`). Then bitwarden was measured once.
  *Tried after, 2026-10-27, and dropped:* reading 2 lines past each page's slice (the right-page rule's margin) at the
  same depth discount. Sonarr stayed at 32/34 (Simkl #8574 moved into the top 20, #8757 fell to rank 24) and bitwarden
  gained one wrong clear winner.
- **The measure** (`--score-index`, no AI; results `benchmark/results/2026-10-27-d87-index-*.json`). A right page's
  walk, at its own depth + 2 and stopping where its slice stops (shared components; endpoints for screens and
  components), reaches a fixed file (D87, agreed 2026-10-27):

  | Repo | Bugs | Have a right page | Hit | Bar |
  |---|---|---|---|---|
  | Sonarr (tuned here) | 40 | 34 | **32 (94%)** | ≥ 95%: **missed by one bug** |
  | bitwarden/server (once, frozen) | 5 | 3 | **3** | all scored bugs: **passed** |

  A random 20 pages would hit about 27% of Sonarr's scored bugs (median 8 right pages of 718) and 40% of bitwarden's
  (median 13 of 1,183; #7003 touches the event service, which 151 pages reach).
- **Clear winners: a risk before wiring in.** 5 on Sonarr, only 2 right. The 3 on scored bugs all hit (2 the winner
  itself, 1 through "also possibly"). The other 2 fell on bugs no page covers (Status page for "Package Version",
  ImportListSync), and would go out with no AI and no "no confident starting point". None on bitwarden, so it was never
  checked there.
- **Every miss, looked at:**
  - Sonarr #8574 "Simkl import list not syncing" (right page at rank 46). Rank 1 is `ImportListSyncService.Execute`,
    the sensible place to start, but its walk stops before the Simkl files.
  - Sonarr #8242 "NFO metadata writer for multi-episode files" (rank 50). The words differ: the ticket says "NFO", the
    code `XbmcMetadata`. Media Management and series pages rank first on "multi-episode". This is D87's stated cost:
    the AI's compact list of every page is the fallback.
- **No page covers this code** (not counted; a gap in Phase 2's pages). Sonarr 6: plugin code reached only through
  DI or past the depth cap (Simkl and Trakt import lists, housekeepers, a database converter, the rejected-import
  service). In 4 of 6, rank 1 is still the right area (Import Lists twice, ExtraService, the Queue API). bitwarden 2: the
  Setup command-line tool, and WebAuthn's 2FA token provider (DI only). On both, a generic `/{controller}/Index/{id?}`
  screen ranks first.
- **Fresh review (D61), same day:** an independent agent reproduced every number and found: plurals and "-ing" didn't
  fold together ("setting" ≠ "settings"); the measure's walk didn't stop at shared components as the pages do;
  `SharedResources.en.resx` (bitwarden's only resource file) was skipped and its keys are looked up by string; a test
  passed without what it tested; bad JSON in a text file and a trace entry missing its section could crash. All fixed
  and tested (12 index tests). Re-measured with the same frozen settings: the same hits (Sonarr 32/34, bitwarden 3/3).
  Only the right-page counts shrank. Left as known: two neutral `.resx` classes of the same name in different
  projects are merged; C# raw strings and `'"'` are read loosely (noise only); `select.py` finds renames with one
  `git diff -M`.
- **Owner's call, 2026-10-28:** the bar failed by one bug, recorded as such (D87). The clear-winner shortcut is dropped
  (it measured 2 right of 5 on Sonarr), and the index is wired in: whenever code finds no starting point, the picker
  sees the top 20 in detail plus every page as a numbered line; the top 20 alone held a right page for the same 32/34
  and 3/3. "No confident starting point" in the bundle, `where` asked in the questions draft. ~11.5k tokens a pick on
  Sonarr (top 20 ~2k, list ~8.5k) against ~28k+ before. A fresh review (D61) found no bugs; its stale-text and test
  findings were fixed. `--score-index` now scores the top 20 for every bug.

### Step 8 · what a bundle reaches of a fix (D87 follow-up) · 2026-10-27 · measured, no AI
The owner asked: when a fix spans related parts (an API, its DTO, a migration, …), does the bundle miss them?
`--fix-coverage` (no AI; `benchmark/results/2026-10-27-d87-fix-coverage-sonarr.json`) starts each Sonarr bug from the
index's best-ranked right page and puts every file the fix changed in the first layer that reaches it. **Upper bound:**
the page's whole slice (its walk and the handlers it triggers) stands in for the written statements' citations, both as
a layer and as the starting points of the impact walk.

| Layer | Files it offers (median) | Fix files it reaches (82 in the 34 bugs with a right page) |
|---|---|---|
| The page's slice | 96 | 47 (57%) |
| + what depends on it, 2 lines back (uncapped) | + 1,150 | + 19 |
| + 📜 usually changes together | + 10.5 | + 1 |
| **Missed** |  | **15 (18%), in 4 bugs** |

- **30 of the 34 bugs have every fix file reached.** Sonarr's fixes are small (median 2 files; the frozen set caps them
  at 15), so this says little about big, many-part changes.
- **What was missed:** 13 of the 15 files are in 2 frontend bugs: 7 CSS files of a theme bug (styles aren't in the
  graph), and a tooltip bug (theme hook, a CSS file and its typings, `package.json`, `yarn.lock`). The others: 1 migration
  (`217_add_mal_and_anilist_ids.cs`) and 1 JSON DTO (`SimklAPI.cs`), which the fixed code *uses*: the impact walk goes
  back to what depends on the code, never forward to what it uses beyond the slice, so no depth would reach it.
- **Looking one step forward wouldn't catch the DTO miss** (checked before building it): `SimklAPI.cs` is used by
  `SimklImportBase.cs`, which is reached only through the impact walk, and the forward walk 2 lines past the page found no
  fix file at all. Forward from the impact layer would add to a list that is already ~1,150 files.
- **Reached is not shown.** The uncapped impact layer is a third of Sonarr's ~3,900 files (inflated here by starting
  from the whole slice), so a bundle lists only 5 per group and the top 10 📜. An estimate of what a bundle would show
  puts 17 reached fix files (in 13 bugs) among those cut, but it isn't a measurement: 47 of the 50 "shown" files are
  the slice itself, and a bundle started from citations fills the capped lists differently. Suggestive only: ordering
  what's close to the ticket may matter as much as new links; a run with written pages would tell.
- The 6 bugs no page covers, started from the index's first page: 8 of their 11 fix files are in its impact layer.
- *Fresh check (D61), same day:* reproduced byte for byte; it found the first write-up overclaimed (a "shown today"
  row that was mostly the slice, and an "ordering beats new links" conclusion), a median rounding, `.lock` and test-path
  labels, and that the slice should be what the page writer sees (walk + handlers). Fixed; counts unchanged.

### Step 8 · paid pilot of browser proof (D86) on 2 bugs · 2026-10-28 · in progress
The first proofs since D86 was built (the 2026-10-26 attempts, before it, are saved locally (untracked, like all of `run/`) in
`benchmark/tickets/sonarr/run/pilot-before-d86/`). `run.sh … prove --only SONB-5,SONB-13 --model sonnet`.
- **SONB-13 (#8842, unit): reproduced, and the test passes on the fix commit ("proof is real").** A unit test in
  `MediaBrowserProxyFixture.cs` fails on the named assertion (the `Authorization` header). $0.13, one attempt. Before
  D86 it ended "not reproduced".
  - Its first try never reached the AI: `tickets.app.build` failed with an empty message. Sonarr's restore turns package
    advisories published after that commit into errors (`NU1902`/`NU1903`, as in 1d), and the benchmark's
    `app/build.sh` hid its output. Fixed: `-p:NuGetAudit=false` (as `tickets.dotnetArgs` already had), and a failed
    step's output now goes to stderr.
- **SONB-5 (#9126, browser): not reproduced, the turn cap.** No related spec (Sonarr has none for Quality Profiles),
  so it looked with the browser: opened the profile, wrote a spec and ran it by turn 12, then spent 5 turns searching
  a saved page snapshot for the size inputs and hit 16 of 15 turns, at $0.23 of its $1. D86 expected this ("the
  15-turn cap binds sooner; the cap is decided after these fixes"): the owner's call.

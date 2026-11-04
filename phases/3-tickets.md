---
phase: 3
name: tickets
status: done
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 4, impact-report: 5, reference-page: 1, source-document: 2, trace-links: 1 }
provides: { ticket-bundle: 2, ticket-update: 1, bug-proof: 1 }
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
  page titles (not the pages; since 2026-10-28 the D87 ranking, top 20 in detail plus every page numbered); `stangent-point-picker` returns up to 5 pages, each with a word-for-word ticket quote; *Replaced 2026-11-02 (D101): the file finder searches the repo and returns files; code's signals only with it off.*
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
- Same inputs → byte-identical bundle. Validated against `schemas/ticket-bundle.v1.json` (*v2 since 2026-11-02, D101: file points, pages that explain them, impact lists cut first*).

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
- **"Don't fix yet"** after "not reproduced" is a label and a suggested next step, not a lock (D75; *replaced by D106: "no proof yet", and why*). The developer can
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
- Subagents: `stangent-point-picker` (*replaced by `stangent-file-finder`, D101*), `stangent-bug-reproducer`, `stangent-question-writer`, `stangent-summary-writer`.
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
- **Proof runs** (D81) on the 30 newest bugs and the 10 not-bugs, each attempt capped at $1 and 15 turns (30 since 2026-10-28, D86) (hitting the
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
- [x] **Bundle: right place:** ≥ 70% of the 40 bugs have a file the fix changed among the starting points' files.
- [x] **Bundle: beats a keyword search:** that hit rate beats the baseline, the 95% range of the gain above zero.
- [x] **Grounded:** every starting point, quote and draft claim passes the code checks; tests show a made-up quote and
      a non-existent page are rejected.
- [x] **Repeatable:** replaying saved MCP results gives byte-identical bundles; a re-run with nothing changed makes
      zero AI calls.
- [ ] **Proof is real:** of the bugs marked reproduced, ≥ 90% pass on the fix commit. *Failed: 77%; the proof is advice only.*
- [x] **Proof finds bugs:** ≥ 50% of the 30 bugs the proof runs on reproduced (unit and Playwright reported apart, D81).
- [ ] **Proof catches wrong reports:** at most 1 of the 10 not-bugs reproduced. *Failed: 7 of 10; the proof is advice only.*
- [x] **Jira round trip** live on the owner's site: read a ticket with comments and an attachment, post a comment (comment 10069, 2026-11-01).
- [x] Reported: how much of each fix the whole bundle covers; bundle size; cost and time per ticket and step.
- [x] If a bar fails, its rule applies (D79): "proof is real" or "catches wrong reports" → the proof is advice only;
      "right place" or "beats a keyword search" → AI picks off by default; "proof finds bugs" → "not reproduced" is
      information only, not "don't fix yet". The report says which and why.
- [x] Tests run without an LLM or a network (canned subagent outputs, the file backend); CI green ([PR #2's run](https://github.com/hohkokyoung/stangent/actions/runs/41470150180)).
- [x] End-of-phase report written; its claims checked against the results files by a fresh agent (D61).

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

### Step 8 · paid pilot of browser proof (D86) on 2 bugs · 2026-10-28 · both reproduced, both real
The first proofs since D86 was built (the 2026-10-26 attempts, before it, are saved locally (untracked, like all of `run/`) in
`benchmark/tickets/sonarr/run/pilot-before-d86/`). `run.sh … prove --only SONB-5,SONB-13 --model sonnet`.
- **SONB-13 (#8842, unit): reproduced, and the test passes on the fix commit ("proof is real").** A unit test in
  `MediaBrowserProxyFixture.cs` fails on the named assertion (the `Authorization` header). $0.13, 12 turns, one attempt.
  Before D86 it ended "not reproduced" because it hit the cap (16 of 15 turns).
  - Its first try never reached the AI: `tickets.app.build` failed with an empty message. Sonarr's restore turns package
    advisories published after that commit into errors (`NU1902`/`NU1903`, as in 1d; seen by building that commit by
    hand, since the old script kept no output), and the benchmark's
    `app/build.sh` hid its output. Fixed: `-p:NuGetAudit=false` (as `tickets.dotnetArgs` already had), and a failed
    step's output now goes to stderr. With the fixed script in the clone, that commit built by hand, then the retry
    reached the AI.
- **SONB-5 (#9126, browser): not reproduced, the turn cap.** No related spec (Sonarr has none for Quality Profiles),
  so it looked with the browser: opened the profile, wrote a spec and ran it by turn 12, then spent 5 turns searching
  a saved page snapshot for the size inputs and hit 16 of 15 turns, at $0.23 of its $1. D86 expected this ("the
  15-turn cap binds sooner; the cap is decided after these fixes"): the owner's call.
- **The owner's call: 30 turns per attempt, $1 kept** (D86, D81; `tickets.proveBudget` default and the Sonarr config).
  **SONB-5 rerun: reproduced, and the test passes on the fix commit.** A Playwright spec
  (`e2e/stangent/profile-size-max.spec.ts`) fails on the named assertion (the size editor's maximum stays below 750)
  in all 3 of stangent's runs, and passes all 3 on the fix. 25 turns, $0.23. The 15-turn attempt is kept locally in
  `run/pilot-d86-turns15/`.
- **Pilot cost:** $0.59 for three AI attempts ($0.13 + $0.23 capped + $0.23). Both bugs reproduced and both proofs are
  real; before D86 neither was (SONB-5's tests timed out, SONB-13 hit the turn cap). Two bugs say the steps work end to end, not how often; the full run (D81) measures that.

### Step 8 · parts outside the code (D83) built before the full run · 2026-10-31 · no AI spent
- **Why before the run:** the earlier Sonarr attempt gave infra tickets (a full disk, a proxy) app pages, an app-code
  bundle and a $1 proof attempt that couldn't reach the problem. Running the paid set without D83 would score a known gap.
- **The owner's rule (D83, settled):** proof is skipped only when every part is outside the code and nothing points
  into it. UI, API and app-level security bugs always get an attempt; a developer removes a wrong tag to force one.
- **Built:** the point picker may return up to 3 outside parts (area + the ticket's words, checked word for word);
  `ticket points --add-outside / --remove-outside`; the bundle's "Outside the code" section (`ticket-bundle` v1 gains
  an optional `outside`, not yet published, so no bump); proof's skip (`not-provable`, `outside: true`, no AI call) and
  no push-back when some part is outside; the questions draft sees the parts and asks `where` only with neither; the
  score lists `outside` per proof. 5 new tests, 275 green.
- **Fresh review (D61), same day:** nothing serious. Six small fixes applied: one call's `--remove-outside` now runs
  before its `--add-outside`; the outside count matches the de-duplicated list; the no-push-back decision reads the
  bundle the attempt was prepared with (saved in the step state); the questions writer is told which parts are the
  ticket's words and which a developer's notes; "proof is skipped" only shows on bugs; a stale picker line fixed.
- **Expected on the next run:** the picker's, reproducer's and question writer's instructions changed, so their recorded
  results are stale: every pick, proof attempt and draft runs again (inside the D81 budget).
- **Not measured yet:** how often the picker marks a part outside, and whether any mark is wrong; the full run shows it.

### Step 8 · full run stopped at SONB-22; our own setup caused most failures (D89) · 2026-10-31 · $5.82 spent
- **What ran:** points, pages and bundles for all 50; proof on SONB-1 to 19 (6 reproduced, 8 "hit the cap", 2 no output,
  2 not-provable, 1 test passes); SONB-20 to 22 never started the app. The owner stopped it to fix first.
- **Read from the transcripts, not guessed:**
  - 6 attempts (SONB-1, 4, 5, 15, 16, 19, part of 17): repo-relative permission rules stopped matching after the agent's
    `cd`, so writes, the result file included, were refused; the retry then got only the leftover turns.
  - SONB-11, 14: backend points stored as pages, so only Playwright was offered for server-side bugs.
  - SONB-20 on: the benchmark's seed broke on older Sonarr (duplicate TvRageId, unique there); serve waited 300 s.
  - SONB-6, 10, 18 really used their 30 turns (re-running their tests 5–9 times): the cap is judged on the next run.
- **Fixed, all general (D89):** absolute rules plus each command back in the repo; refusals of what stangent allows
  recorded as `setupBlocked` and the batch stopped; a dying start command reported at once with its error; unit targets
  from the code behind every starting point. Benchmark only: the seed gives each series its own ids (checked at
  SONB-22's commit: the app starts with both series).
- **Fresh review (D61), same day:** six findings, all fixed: a refused command line counts as blocked only when all of
  it is allowed (`dotnet test … && rm -rf x` refused for the `rm` is the rules working); code behind a page only
  widens the test-target search when the points' own code has no unit test, so it can't push the right project out of
  the top 3; a start command that exits 0 is waited for as before (fail fast only on a non-zero exit; the start command
  should stay in the foreground); the plugin's instructions cover a setup-blocked proof; `spec-run.sh` uses the real
  path; D89's cost names the re-run a target change causes. Its mutation check found 4 paths no test caught; tests
  added for each. A second check found 3 small gaps (a single `&`, a `cd` out of the repo, one untested guard), fixed with tests. 17 new tests, 292 green.
- **Next:** proof from SONB-20 on; if smooth, SONB-1 to 19 again; then drafts, replay, score.

### Step 8 · run #2 stopped at SONB-27: the machine, not the AI (D90) · 2026-11-01 · $6.62 spent
- **SONB-1 to 26 with D89:** 15 reproduced (SONB-1 to 22: 15 of 19, was 6). On the fix 4 don't pass (SONB-1, 11, 14
  still fail; SONB-2 doesn't compile): to read before scoring.
- **Stopped:** from SONB-23 the commits target .NET 8, missing on this Mac; the empty report read as "no test named …
  ran" (SONB-23 to 26). A shared build folder also kept a newer NUnit adapter that finds no tests in older builds.
  SONB-19's Moq `Verify` failure wasn't counted as an assertion.
- **Fixed (D90):** runner failures are setup failures and stop the batch; mock checks are assertions. Benchmark: a .NET
  with the 8 runtimes, shared build folders cleared between commits (checked: 308 tests run at SONB-27's commit).
- **Fresh review (D61):** 5 findings, all fixed: the app scripts assumed `_output/net10.0`, so at .NET 8 commits the
  browser proofs had served another commit's app (SONB-23 to 27, already set aside); a test that crashes the host
  read as a runner failure (now confirmed by listing the project's tests); strict-mock refusals counted as
  assertions; a runner failure on the fix commit was scored as "proof not real"; a two-framework edge case. 304 green.
- **Trial SONB-19, 23 (setup clean):** SONB-19 reproduced (Moq `Verify`), real on the fix. SONB-23's correct NUnit
  `Assert.AreEqual` test was rejected: NUnit trims its frames, so only "Expected: 2 / But was: 1" showed. Fixed
  (D90), 306 green; SONB-23 set aside and proved again.
- **Second fresh check (D61):** the runner check never fired on real output: VSTest prints its "available" header
  even when it finds nothing, and the fakes lacked it; MTP with no runtime writes no report and wasn't checked. Fixed
  (a test counts as listed only when a name follows the header; MTP's "Discovered 0 tests"; a no-report run is
  checked too) and confirmed on saved real output (missing .NET 8, MTP, a normal project). The app scripts' "found N"
  message now prints. 307 green. Run #3 keeps the binary it started with (no swap mid-run); the machine causes are
  fixed, so the safety net isn't needed there: its results get a manual look for runner failures.

### Step 8 · full Sonarr run scored (D79) · 2026-11-01 · ~$19 this run (+ ~$13 on stopped runs)
Score: `benchmark/results/2026-11-01-phase3-sonarr-score.json`; every value re-derived from the raw files by a fresh
agent (D61), all match.

| Bar (D79) | Result | |
|---|---|---|
| Right place (starting files reach a fix file) | 18/40 = 45% (range 30–60%) | ❌ (≥70%) |
| Beats a keyword search | 45% vs 10%, gain 0.35 (0.18–0.53) | ✅, fragile (below) |
| Proof finds bugs | 22/30 = 73% (backend 17/21, frontend 5/8) | ✅ (≥50%) |
| Proof is real (passes on the fix) | 17/22 = 77% | ❌ (≥90%) |
| Catches wrong reports | 7/10 not-bugs "reproduced" | ❌ (≤1) |
| Grounded (must-pass) | 19 of 90 drafts missing: 18 failed their claim check, SONB-4's pull hit `keychain_failed` | ❌ |
| Repeatable (must-pass) | 50/50 bundles identical; 19 steps would re-call the AI (all failed drafts) | ❌ |

- **Not-bugs:** the reproducer asserts the reporter's expectation (SONB-43 "no auth required", by design in v4); the
  code does otherwise, so the test fails and counts as proof. A failing test shows the code differs from what the
  reporter expects, not that it's a bug.
- **Not real on the fix:** SONB-2 no longer compiles (the fix added a constructor parameter); SONB-1, 11, 14, 30 fail
  the same way on the fix: they test something the fix didn't change.
- **Right place misses:** SONB-1, 3, 4, 5, 11, 15, 17–23, 25, 28, 29, 31–33, 35, 37, 40. Missing pages were never
  generated (run.sh passed page ids with spaces unquoted): 19 bugs had unwritten starting pages; by graph reach that
  would add about one hit (≈47%), so the bar fails either way.
- **Keyword baseline is weak:** one bag of all ticket words ranked by raw count; big files (FileNameBuilderFixture.cs
  in 32 of 40 top 5s) win. A summary-only, IDF, no-tests baseline gets 16/40 and the gain's range crosses 0. Not
  changed after the run (D57); stated as a caveat.
- **Drafts:** most failures are headings or summary lines ("Fix: …", "Resolution: …") the claim check reads as
  claims without a source; some are real unsupported statements; one invalid FSD link. `keychain_failed` on SONB-2/4
  came from 4 parallel pulls reading the OS keychain.
- **Harness fixes during the run:** drafts run 4 at a time (`--jobs`); the wait was in the wrong shell (replay began
  before the last drafts ended; fixed, replay re-run); 28 stale Oct 26 draft files moved to `run/stale-drafts-oct26/`.
- **What follows (D79, preset):** proof ships as **advice only** (can't block or clear a fix); AI starting-point picks
  ship **off by default**. Grounded and repeatable are must-pass: bugs to fix and re-run, not results.

### Step 8 · after D92: pages written, drafts re-run, re-scored · 2026-11-01
`benchmark/results/2026-11-01-phase3-sonarr-score-d92.json` (no new proofs). 42 missing pages written (1 failed).
- Right place 47.5% (range 32–62%), as expected: still ❌. Beats keywords ✅. Proof bars unchanged.
- Drafts: resolution 30/30 pass (was 25); questions 43/50 (was 35/48). Replay: 50/50 identical; 7 re-run calls, all
  the 7 failed questions drafts. Grounded and repeatable still ❌, by 7 drafts.
- The 7: the questions writer still restates the ticket unlinked ("The ticket mentions…", "The environment lists…"),
  and one polite request ("We'd also like the full logs") reads as a statement because it doesn't end in "?".
- **Second draft fix (D92, same day):** the questions writer links what it repeats or asks straight away; a request
  ("Please send…", "Could you…", "We'd like…") counts like a question. Drafts re-run: questions 50/50, resolution
  30/30; replay 50/50 identical, **0 re-run AI calls**. Final score
  `benchmark/results/2026-11-01-phase3-sonarr-score-final.json`: grounded ✅, repeatable ✅ (must-pass).
- **Jira round trip (must-pass):** reading done live (all 50 pulled through `ticket pull`); posting was proven in the
  step 0 spike (2026-10-25) but not yet through `stangent ticket post`, and the narrow-scope OAuth sign-in (D82) is
  still unconfirmed. Both need the owner (posting writes to their Jira).
- **Fresh check of the final must-pass bars (D61):** grounded ✅ (50 questions, 40 resolution drafts; 94 ticket quotes
  verbatim; 114 file and 31 page links real) and repeatable ✅ (50/50, 0 calls) confirmed from the raw files. The
  request rule let 3 sentences through, none false. **Found:** `bug.missing` lists `version` for 49 of 50 tickets,
  though 41 give one under Environment ("Sonarr: 4.0.11.2815"): the pull doesn't read a version out of an environment
  section, so drafts ask for a version already given, and a `missing:version` link backs "the version field is empty"
  (SONB-17, 38). To fix (general: a version-like number in the environment counts as the version).
- **Jira round trip ✅ (2026-11-01, owner's OK):** SCRUM-7 pulled live, its questions draft made and checked (one
  claim linked to the ticket), shown to the owner, then `stangent ticket post SCRUM-7 questions` posted it as comment
  10069 under the owner's login; a fresh pull reads it back. (The benchmark clone accepted SCRUM only for this; its
  settings were restored.) Still open from step 0: a sign-in approved with only D82's Jira scopes.
- **All three must-pass bars now pass** (grounded, repeatable, Jira round trip). The quality bars that failed take
  D79's preset fallbacks: proof ships as advice only; AI starting-point picks ship off by default.

### Step 8 · after D91, D93, D94: re-run (no new proofs) · 2026-11-01
`benchmark/results/2026-11-01-phase3-sonarr-score-d94.json`. Pull re-run (D93: `version` now missing on 4 of 50, was
49), picks re-run where the picker's example changed, bundles and drafts re-made, replay.
- Right place 47.5%, beats keywords ✅, proof bars unchanged (no new proofs).
- Drafts: 7 of 90 failed both tries this time (5 questions, 2 resolution), a different 7 from before (the run before
  had 0): the AI often adds one unsourced line on its first try ("Thanks for the detailed report.", "We couldn't
  reproduce this", a context sentence) and its retry usually, but not always, fixes it. So grounded/repeatable pass or
  fail by chance with one retry. Open for the owner (below).
- Other draft check failures seen on first tries: linking `missing:steps` when steps are given; FSD sections written
  "§§2" instead of as listed.

### Step 8 · after D97, D98: drafts re-run, re-scored, right place by stage · 2026-11-01
`benchmark/results/2026-11-01-phase3-sonarr-score-d98.json` (no new proofs; old drafts in `run/before-d98/`).
- **Drafts:** 90 of 90 recorded (the run before: 7 failed both tries), 5 retries in all. Grounded ✅, repeatable ✅
  (50/50 identical, 0 re-run AI calls). 23 parts left out in 13 drafts, 166 kept with no claim (listed as unchecked).
- **What was left out:** 17 plain section titles with no `##` ("What changed", "How it was checked"), so those drafts
  lose their headings; 6 good questions ("Could you confirm …", "(press F12 …)", "… so we can see …", "Roughly how
  many …?"); one unlinked "We have no questions before starting work.". Nothing false reached a draft.
- **Kept with no claim:** headings and lead-ins ("## What changed", "A few questions before we start:"), greetings,
  and pure asks.
- **Right place by stage** (`reported.rightPlaceStages`, new, no AI; first stage each bug's fix is lost, of 40;
  checked by a fresh agent, D61, who found the first cut mislabelled 5 bugs as picker misses):
  hit 19 · no page covers the fix 8 (SONB-11, 17–19, 29, 32, 33, 35: fix files in the graph but in no page's slice) ·
  right page not in the top 20: 2 (SONB-22 #57, 23 #95) · **code's own points skipped the picker** 5 (SONB-3, 25, 28,
  37, 40: URL/title/symbol signals found points, so the AI never ran, D70; a right page was at #1–#11) · picker missed
  it 2 (SONB-20, 21) · right page picked but not written 1 (SONB-15) · right page picked, its statements cite no fix
  file 3 (SONB-4, 5, 31). The impact walk reaches a fix file in 20 bugs (the hits + SONB-21); the bundle shows one in 14.
- **Recall by layer, and how much each layer offers** (median files per bug; a fix changes 2): starting files 47.5%
  (19/40, 18 files) · the impact walk, 2 lines back + 📜, 50% (20/40, 158 files) · what the bundle shows (pointers) 35%
  (14/40, 15 files): 5 hits have the fix file among the starting files but not among the pointers. From the best
  right page instead (D87 fix coverage, 2026-10-27), the walk reached every fix file in 30 of 34 bugs: expansion works
  from a right start and adds nothing from a wrong one.
- **What it means:** following the code further barely helps (20 vs 19). Fixing every ticket-side stage (5 skipped +
  2 missed) gives at most 26/40 = 65%; the 70% bar also needs the page side (1 unwritten, 3 not citing the fix).

### Step 8 · the free checks: bundle keeps cut pages' files; the nearby-page walk · 2026-11-02
`benchmark/results/2026-11-02-phase3-sonarr-score-bundle-fix.json` (bundles re-made, no AI; old bundles in
`run/before-bundle-fix/`; drafts and replay records not re-run, so they predate these bundles) and
`benchmark/results/2026-11-02-nearby-walk-judged.json`. Both checks were reviewed by fresh agents (D61), whose findings
changed the walk (below).
- **Bundle fix (D71, fixed):** a cut page now keeps its cited files among the pointers. The bundle shows a fix file in
  **19 bugs (was 14)**: SONB-6, 14, 27, 38, 39. Right place unchanged (47.5%: it counts starting files). **Cost:** median
  pointers 7k → 14k characters (27 → 54 pointers, median 30 files) and median files shown 15 → 29; the cap is missed in
  19 of 50 bundles (was 5) and every page is cut in 25 (was 18), so in those bundles the page statements go and the cap
  still fails, while the impact answer (median 11k) is never cut. What gives way under the cap is open for the owner.
- **The nearby-page walk** (`NearbyWalk`, reported as `nearbyWalk`): on the 12 page-gap bugs, start from the real fix
  files (tests explaining, not finding), walk the slice's own lines in reverse with the hub stop, take the nearest
  written page, ties ranked by the D87 index. The first cut walked the impact walk's lines (including `triggered-by`,
  which leads to downstream handlers) and scored "lands on a right page", which D87's +2 margin almost guarantees; the
  fresh review caught both. Corrected: lands on a right page 5, right page not written 1, no right page 3, dead end 3.
- **Does the page help? (fresh agent, all 9 picks; strict):** helps 1 (SONB-15), partly 7 (same area, nothing on the
  mechanism the fix changes), no 1 (SONB-31, manual import for an automatic-import bug). With the 3 dead ends:
  **the nearby page explains the fix in 1 of 12.** The import list sync page went to 4 bugs (SONB-11, 17, 29, 33) whose
  fixes are all in Simkl/Trakt provider code it never describes.
- **Only 3 of the 9 picks lie within their page's own slice depth** (SONB-4, 5, 31; `withinOwnDepth`): the walk has
  no depth limit, so most picks are pages whose slices never reach the fix (SONB-11, 29, 33 at depth 9–10).
- **Dead ends** (where the graph work should aim; a second fresh review rebuilt the walk independently and agreed):
  - SONB-18: database converters, migrations and a static constructor: code only the framework runs, nothing calls it.
  - SONB-32: `HousekeepingService.Execute(HousekeepingCommand)` has no `triggered-by` line (an `IExecute<T>` command
    handler not detected), so the walk runs on to an unwritten handler page (`CommandExecutor.Handle(ApplicationStartedEvent)`,
    depth 7) and the `IEventAggregator.PublishEvent` hub.
  - SONB-35: stopped at `CompletedDownloadService.Import`, a "hub" only because 16 of its 17 callers are tests: slices
    count test callers towards `spec.hubCallers` (Slice.cs). A walk without the hub stop reached the monitored-downloads
    page, which a first judge rated "helps".
- **What it means:** a nearby page gives area context, rarely the mechanism. For code no page covers, the file pointer
  is the evidence and a nearby page is at best labelled context; it doesn't close the 12-bug gap. Provider and plugin
  code (dispatched by interface or name) and framework-run code (converters, migrations, housekeepers) are the gaps.
- **Caveat:** SONB-4's page was written at a later commit than the fix, so it describes the fixed code: a benchmark
  artefact (pages from the pinned commit, bugs from older commits) that can flatter or mislead any page-based check.

### Step 8 · does the planted FSD cover the not-bugs? · 2026-11-02 · no
None of the 10 not-bug tickets (SONB-41–50) is about behaviour the planted FSD (`benchmark/fsd/sonarr/`, 15 screens)
describes: an indexer's results, a required password, library import, anime episode parsing, table sort memory, bulk
moves, memory, CPU and disk space. The nearest, SONB-42 (a Trakt link on series details), falls in §1.2, which says
nothing about links; only its bundle carries FSD sections (§1, §1.2). D79's "catches wrong reports" can't be judged
against an FSD on Sonarr, so D99 (and D96's FSD path) can't be measured here.

### Step 8 · agent baseline pilot: 9 of 10 bugs we miss · 2026-11-02
`benchmark/results/2026-11-02-agent-baseline-pilot.json` (+ transcripts), `benchmark/tickets/agent_baseline.py`.
Headless Claude Code (Sonnet), given only the ticket's text and the code at the fix's parent commit (`git archive`, no
history), Read/Grep/Glob only, reads of the stangent repo and benchmark folders denied, 30 turns / $1 cap (D86's), names
up to 10 files. Run on the 10 newest of the 21 Sonarr bugs our pipeline misses (SONB-3, 4, 5, 11, 15, 17, 18, 19, 20, 22).
- **9 of 10 hit, every hit at rank 1**; the one miss, SONB-18 (a database converter, the walk's dead end too), named
  frontend language files. 4–7 turns each, **$0.40 in all ($0.02–0.09 a ticket)**, no read outside the export.
- **It searched, it didn't guess:** each run greps the ticket's own words (error messages: "unsupported content type",
  "Unable to find matching section location", "must not be empty"; names) and its top pick appears in its tool results.
- **Caveats:** 6 of the 10 tickets name the fix file's class (information a real ticket carries; our pipeline still
  missed them, mostly through D70's code-only shortcut or page coverage). Sonarr is public, so the model may know its
  code; bitwarden is public too, so only a private repo rules that out. 10 bugs, the newest misses, not a random sample.
- **What it means:** agent search finds what pages can't, cheaply. The full baseline (Sonarr's 40, bitwarden's 24
  held out, about $3 at this rate) is the next measure.

### Step 8 · agent baseline, full: 36/40 on Sonarr, 23/24 on bitwarden held out · 2026-11-02
`benchmark/results/2026-11-02-agent-baseline-{pilot,sonarr-rest,bitwarden-heldout}.json` (+ transcripts). Same set-up
as the pilot. A fresh agent (D61) re-scored every case, checked each export was the fix's parent (all single-parent;
no fixed lines present before), listed all 267 tool calls (no read outside an export: the 3 flagged paths were the
agent's own folder mistyped, and failed) and found no answer key, stangent file, git history or fix diff in any result.
- **Sonarr (tuning set): 36 of 40 (90%)**, 35 at rank 1; our pipeline: 19 (47.5%). Both find 18; the agent alone 18;
  ours alone 1 (SONB-1); neither 3. Misses: SONB-18 (crash site vs a database converter), SONB-1 (named the command
  serializer, fix in the API resource), SONB-21 (path helpers, fix in DiskTransferService), SONB-32 (the extra-files
  code, fix in the housekeepers).
- **bitwarden (held out, D100): 23 of 24 (96%)**, 21 at rank 1. Strict: 3 tickets link the fixing PR in a comment
  (#5223, #2506, #1367; the agent had no web or git, so only the number); without them 20 of 21. #4801 and #3116 share
  one fix.
- **Files handed over:** median 4 named per bug (our bundle: 29 files shown, 18 starting files).
- **Cost: $3.11 for 64 tickets** ($0.05 average, $0.145 most), 4–17 turns, all within the 30-turn / $1 cap.
- **Caveats:** about half the hits come from tickets that name the file (Sonarr: 8 of 36 give the full path, 10 more
  the file or class name; bitwarden: 10 of 23 the full path, 1 the name), which real tickets do and our pipeline still
  missed. Both repos are public, so the model may know them; every hit file was found in its own search results, but
  only a private repo rules prior knowledge out. Runner hardened after the run (reads of `~/.claude` and the session
  temp folder denied; tool calls counted in full); neither was used in these runs.
- **What it means:** the 70% bar passes by a wide margin on both sets with agent search alone, held-out included. Our
  own search would need to close a 47.5% → 90% gap to compete; the agent costs about $0.05 a ticket.

### Step 8 · D101 built: the file finder; right place 90% · 2026-11-02
`benchmark/results/2026-11-02-phase3-sonarr-score-d101.json`. Built (owner, asleep: "go with the most optimal option"):
`stangent-file-finder` replaces the page picker; `ticket-bundle` v2; `ticket-run.sh` runs the finder in a throwaway
git worktree; run.sh's `files` phase searches each ticket at its run commit (bugs: the fix's parent). Three fresh
reviews (D61) before and after the paid run; every finding fixed (cache key, isolation, subfolder roots, cleanup,
bundle cost, status, retry budget, boilerplate lines, rejected files shown, stale answers shown).
- **Right place 36 of 40 (90%, 95% range 80–97.5%) ✅** (was 19, 47.5%). The misses are the agent baseline's own: SONB-1,
  18, 21, 32. The bundle shows a fix file in 36 (was 19); the impact walk from the found files reaches one in 37.
- **Bundles:** none over the 40k cap (was 19 of 50); median 29k characters (was 39k); impact lists left out in 25;
  median 14.5 files shown (was 29). Pages that only explain a file left out under the cap: median 0, most 17. Missing
  pages listed: median 0, most 4.
- **The finder:** all 50 searched (status found), $2.68 in all (most $0.13 a ticket), 8 needed a retry, 4 tickets kept
  a rejected file (SONB-1, 6, 18, 42), listed in their bundles.
- **Unchanged here:** proof bars (no new proofs: 77% real, 7/10 wrong reports "reproduced"), and the drafts and replay
  records, which predate these bundles (so "repeatable" is from the earlier run).
- **Final fresh check (D61):** re-scored 36/40 from the clone independently; every search ran at the bug's run commit;
  every named file existed there; 61 transcripts: all worked in a worktree, none touched `.stangent`, 5 reads mistyped
  outside the worktree were refused. Fixed after it: auto memory off for every headless call (it was on, its folder
  empty), the commit recorded without `+dirty`, two test transcripts removed. Re-scored after: unchanged.
- **Owed:** the full pipeline on bitwarden's held-out 24 (D100; only the bare agent baseline ran there: 23/24), which
  needs bitwarden set up for tickets (replay files, graph, pages); a private repo before trusting the rate outside
  public code; the misses grouped by cause (crash site vs root cause, framework-run code).

### Step 8 · D101 on the held-out set: bitwarden 20/24 · 2026-11-02
`benchmark/results/2026-11-02-phase3-bitwarden-heldout-score-d101.json`. The first full-pipeline run on D100's frozen 24
(owner's OK, $1.28). `freeze.py` laid `heldout.json` out as Sonarr's set (text unchanged; authors and dates from GitHub;
key = the merge commit's own files, equal to the agent baseline's on all 24); `--copy-to-jira` put them in project BWB
(owner: refused layouts send only the refused lines as code: BWB-17's description, one BWB-22 comment); `run.sh points`
pulled them, `files` ran the finder (Sonnet) at each fix's parent. Config `benchmark/configs/bitwarden.tickets.yaml`:
product defaults, no pages, no repo hints. `run.sh` now copies an SDK pin only for a repo that has one (it always
copied Sonarr's).
- **Right place 20 of 24 (83%, 95% range 67–96%) ✅** (bar 70%); keyword search 3 of 24 (gain 71 points, range 50–88).
  Every hit at rank 1. Strict (3 tickets link the fixing PR: BWB-3, 16, 23): 18 of 21 (86%). Proof, drafts and replay
  not run here.
- **Below the agent baseline's 23 of 24.** The finder names fewer files (median 2; the baseline 4): in BWB-4, 5 and 18
  the baseline reached the fix file at rank 1–3 in a longer list; the finder stopped at the service layer (BWB-4), the
  templates (BWB-5) or the validation (BWB-18). BWB-16 (#2506) both miss with the same two files. Likely cause: the
  instructions' "fewer is better when you are sure" plus the per-file quote and line; on Sonarr the two matched (36 each),
  so the gap showed only here. Not changed: per D100 a fix is tried on Sonarr first.
- **Fresh check (D61):** re-scored 20/24 independently (scorer output byte-identical); layout, key and Jira mapping
  correct for all 24; every search at its run commit, every named file, quote and line verified; 27 transcripts (3
  retries) used only Glob/Grep/Read in their own worktree, none touched `.stangent`, `.git`, the benchmark or the key;
  3 mistyped absolute reads refused. It found:
  - **Jira silently dropped a comment's words** (BWB-3 comment 7: a quote holding an image; only "rsulzenbacher wrote:"
    is left). No error, so the copy's fallback never saw it; the copy has no read-back. BWB-3 was a hit.
  - **The pull drops link URLs** (and image sources, list numbers, checkbox marks, code-fence languages) when it turns
    Jira's HTML into text: the raw MCP result holds `pull/5751`, the finder reads "PR up". Reference links were lost in
    BWB-9, 13, 19, 21, 23 too. Product behaviour, so Sonarr's tickets are affected the same way.
  - Smaller: an interrupted copy can leave an issue short of comments (the key is saved first); the score's timing has
    no finder step (`ms.points` is the pull). Fixed: `freeze.py` splits paths by line; `run.sh` logs a missing SDK pin.
- Seen, not fixed (held-out): bitwarden's "Build Version" heading isn't read as the version (D93).

### Step 8 · D102: HTML tickets read as Markdown; re-run · 2026-11-02
`benchmark/results/2026-11-02-phase3-sonarr-score-d102.json`, `…-bitwarden-heldout-score-d102.json`. The pull now turns a
body Jira sent as HTML into Markdown (`JiraHtml.ToMarkdown`) instead of plain text: links, images, list numbers, task
ticks and code marks kept. 16 tickets are HTML-format (Sonarr 5 of 51, bitwarden 11 of 24); both clones re-pulled, and
`run.sh files` re-searched exactly those (their finder input changed), $0.84.
- **Right place: Sonarr 36 of 40 (90%), unchanged** (the 5 kept their hit or miss); **bitwarden 21 of 24 (88%, range
  75–100%)**, was 20: BWB-18 miss → hit. Its text only gained step numbers, image links and italic marks, and its
  earlier search had already seen the controller; its first attempt this time named it: run-to-run variation, not D102.
  So D102 fixes lost text (BWB-3's fixing-PR link, 45 links, 121 code marks) without moving right place.
- **Fresh checks (D61):** a code review before the run (no word or URL lost on the 93 real bodies; its findings fixed:
  D102's note) and a check after it: exactly the 16 changed; no word lost in any; every new answer at its run commit
  with its files, quotes and lines verified; transcripts confined to their worktree; re-score matches. It found the
  loosened quote check rejected a short quote that copies the line's own mark ("1. Save it"): fixed, both forms accepted.
- **Noted, not changed:** re-running `points` at the pinned commit marks every kept answer `stale` (benchmark only:
  the finder runs at each bug's own commit; scoring unaffected); Sonarr's 45 kept answers still record `+dirty`
  (from before that fix); the code-line rule ("two words") rejects one-token lines such as `model.ValidateEdit();`,
  which caused all 3 retries and cost BWB-9 one file (still a hit).

### Step 8 · D96: the honest result · 2026-11-02
No AI and no run. `reproduced` now reads as "the code differs from what the reporter expected, a person decides";
`--confirm` means the reported problem and a bug; a questions draft gets the expected result and is told not to call an
unconfirmed proof a bug (an instruction, not a code check). Document verdicts wait for D99. Scoring is unchanged:
`catchesWrongReports` still counts not-bugs reproduced (7/10 on the saved run).

### Step 8 · does naming fewer files cost the finder anything on Sonarr? · 2026-11-02
No AI: the saved D102 finder answers against the saved agent baseline, on the tuning set only (D100). Not fresh-checked.
- **Same 4 misses** (SONB-1, 18, 21, 32); neither finds a bug the other misses. The baseline's hits are at rank 1 in 35 of 36.
- **Files named:** median 1.5 vs 4. **Wrong files per ticket:** 0.62 vs 2.75. **Share of the fix named:** 66% vs 72%; on the
  21 multi-file fixes, 40% vs 52%.
- On bitwarden, 2 of the finder's 3 misses are ones the baseline reached (BWB-4, 5); BWB-16 both miss (the earlier "3" counted BWB-18, since a hit).

### Step 8 · why the finder misses; git history as extra evidence · 2026-11-02
No AI. The 5 misses D101 names (SONB-1, 18, 21, 32; bitwarden BWB-16); BWB-4 and 5 left unread, to keep the held-out set unspent (D100).
- **On most, the finder names a place near the symptom and the fix is elsewhere.** Far from the symptom: SONB-18 (a frontend error; the fix is a backend converter), SONB-32 (a crash on duplicate rows; the fix is new cleanup jobs). A choice of where to fix: SONB-1 (the converter that recurses; the fix is the API resource), SONB-21 (the unsanitised path, as a ticket comment also proposes; the fix is the disk transfer service); SONB-18 and 32 could count here too. Wrong reason: BWB-16 (it blames the import not filling the folders; the code fills them, and the fix is two queries computing the value the ticket is about). The grouping is a judgement, not a measure.
- **SONB-32 can't be hit:** all 4 fix files are created by the fix, so they don't exist at the commit searched. Sonarr's realistic maximum is 39, not 40.
- **Whole-bundle coverage is 0 on all 5.** Across Sonarr's 40 the bundle covers 71%, but it points at a median of 14.5 files, not the finder's 1.5 or the baseline's 4, and uses the graph and pages at the pinned commit, after the fixes. So 66%, 71% and 72% aren't like for like; the gain comes from 4 tickets and rescues no miss.
- **Git history, one crude code-only probe:** the top 10 files from past commits sharing at least 2 title words, at each bug's parent. Sonarr: right file in 13 of 40, all bugs the finder already finds, 0 of its 4 misses, about 8 wrong files per ticket; BWB-16 not found. Too crude to judge history by: 46% of its files no longer exist, and filtering those out gives 18 of 40 and finds SONB-21 (7.58 wrong per ticket). Real precedents are invisible to it (BWB-16's fix files were touched by "fix ef query joins"). History stays untested, not ruled out.
- **Fresh check (D61):** the numbers reproduce (probe re-implemented: 13 of 40, 8.25 wrong); its corrections are above. The finder's wrong-files and share-of-fix figures also hold.

### Step 8 · audit of all 40 proof results · 2026-11-02
No AI and nothing run: a fresh agent read the raw records of the 2026-10-31 run (before the finder and without D95's checks). Inferred where it says "would". Corrects earlier notes here, including "they test something the fix didn't change" and "really used their 30 turns".
- **The cap compares two units.** `--max-turns` limits model rounds; `num_turns` on a normal finish is tool results + 1, so parallel calls inflate it. `BugProof.CapHit` threw out 6 finished answers unrun: SONB-6, 9, 18, 29, 46, 47 (22-29 real rounds, `success`). Only SONB-28 really ran out; SONB-10's retry was cut to 3 rounds by the same count. Fixed by D104; the saved results don't change until a re-run.
- **Rescuing them adds few real proofs:** SONB-29 would pass on its fix; SONB-6, 9, 18 would still fail. About 26/30 reproduced, 18/26 passing on the fix.
- **The on-fix check is wrong both ways.** Of the 5 "bad" proofs: SONB-1, 11 test the wrong thing; SONB-14, 30 are mis-scored (SONB-30's reported exception is gone, a different one fails); SONB-2 never compiled on the fix. One "good" proof, SONB-26, passes for an unrelated reason. By reading, 18-21 of the 22 are real (82-95%), which straddles the 90% bar.
- **"Not planned" isn't "wrong report".** Of the 10: SONB-44 a real bug fixed later, SONB-42 probably real elsewhere, SONB-47 unknown, SONB-48 outside the code; of the 6 clear non-bugs, 5 "reproduced".
- **"Not reproduced" says almost nothing:** true of a real passing test only on SONB-46, of 9. SONB-16's test reached the bug but passes when run alone (cause inferred). SONB-17 was offered only Playwright.
- **Also:** bundles built at the pinned commit can name code from after the fix (SONB-17, 23, 25, 26, 29; no sign the AI relied on it). Seed and setup problems count against the AI (SONB-10, 18, 28, 46). Starting files don't separate good proofs from bad: the AI reached a fix file in all 17 good ones, 8 of them unnamed by the bundle.

### Step 8 · D105: the on-fix check says what kind of failure it was · 2026-11-02
No AI and nothing run: `--verify` now records `verification.result`, and the score reads it (a record from before D105 is classified from its saved runs). The saved run re-scored: 17 of 22 pass (77%, D79's bar, still failed), up to 19 of 22 (86%) if the unclear ones are real. `same-failure` SONB-1, 11, 14; `different-failure` SONB-30; `not-compiled` SONB-2. These saved proofs have no `shows`, so they compared whole messages; the `shows` comparison first meets real proofs in the re-run.
- **Disagrees with the audit on SONB-14:** "mis-scored" was too kind. The failure it shows is real, but the test can't confirm the fix, which is this check's job, so it stays `same-failure`.
- **Doesn't catch SONB-26:** the test sets no `AirDateUtc`, so the fixed code returns early and it passes for the wrong reason.

### Step 8 · D106, D107, D108: not reproduced says why; the finder's guess; stale drafts · 2026-11-02
No AI and nothing run. `noProof` on the saved run: 8 `ran-out`, 1 `test-passed`. The finder's reason no longer reaches the reproducer and is labelled for people. `ticket post` refuses a draft whose inputs changed. Saved questions drafts are stale now, so they would be made again before posting.

## End-of-phase report

*2026-11-02 · first report (D109).* Phase 3 is **done with two bars failed**, so the proof ships as **advice only** (D79). All three must-pass bars passed when last measured (below). CI green; merged as [#2](https://github.com/hohkokyoung/stangent/pull/2).

### What was built
- **Commands:** `stangent ticket pull | points | bundle | prove | draft | post | check`, `stangent jira login`; `/stangent:ticket`; `plugin/scripts/ticket-run.sh`. See `docs/tickets.md`.
- **Contracts:** `ticket-bundle` v2, `bug-proof` v1 (optional fields added by D95, D105, D106), `ticket-update` v1.
- **In one line:** a Jira ticket becomes a checked starting position: where to look (D101), the context around it, for a bug a failing test, and comments a person posts.

### Benchmark results
Sonarr's 40 bugs and 10 not-bugs (D78; the tuning set since D100), with bitwarden's 24 bugs held out for the finder only. `benchmark/results/2026-11-02-phase3-sonarr-score-d102.json`, `2026-11-02-phase3-bitwarden-heldout-score-d102.json`. D6's three repos aren't the measure here (D77).

| Bar (D79) | Result | |
|---|---|---|
| Right place | Sonarr 36/40 = 90% (range 80–97.5%); bitwarden 21/24 = 88% (75–100%) | ✅ |
| Beats a keyword search | Sonarr 90% vs 10%, gain 80 points (65–92.5); bitwarden 88% vs 12.5%, gain 75 (58–92) | ✅ |
| Grounded (must-pass) | no failed step; 90 of 90 drafts recorded, as last measured | ✅ |
| Repeatable (must-pass) | 50/50 bundles identical, 0 re-run AI calls, as last measured | ✅ |
| Jira round trip (must-pass) | comment 10069 posted and read back, 2026-11-01 | ✅ |
| Proof finds bugs | 22/30 = 73% (57–87%): 17 of 18 with a unit test, 5 of 5 with Playwright, 7 with no test recorded | ✅ |
| Proof is real | 17/22 = 77% (59–91%), up to 19/22 = 86% if the unclear ones are real (D105) | ❌ |
| Catches wrong reports | 7 of 10 not-bugs reproduced | ❌ |

Reported, no bar: the bundle covers 71% of each fix on average (range 59–82%), median 14.5 files shown; median bundle 29k characters, the most 39.5k, all under the 40k cap; recorded AI cost on Sonarr $16.00 (finder $2.68, proof $9.72, drafts $3.60), not counting pages or stopped runs. **Per ticket:** finder about $0.05 and 10 s; bundle 1.2 s (median); proof about $0.24 and 2.6 min (median), capped at $1; each draft about $0.04 and 14 s (median).

**How far to trust these:**
- **The proof numbers are out of date.** They come from one run, before the finder, D95, D104, D106 and D107. D95's `shows` check hasn't met a real proof on Sonarr, so it's built, not measured. The re-run is owed (D109).
- **The proof was measured on Sonarr only,** the tuning set (D100). It never ran on bitwarden.
- **Both repos are public,** so the finder's rate may lean on what the model already knows, and no private repo was tried. About half the plain agent's hits come from tickets that name the file or class.
- **A plain agent finds as well:** 36/40 and 23/24, with the same 4 misses on Sonarr. The finder is behind on bitwarden (21 vs 23) and names less of each fix (Sonarr 66% vs 72%; on fixes of several files 40% vs 52%). It names fewer files (median 1.5 vs 4 on Sonarr) and fewer wrong ones (0.62 vs 2.75), and adds checked evidence and a saved answer.
- **By reading, not measured:** 18 to 20 of the 22 proofs look real, so the true figure may sit on either side of the 90% bar. SONB-26's pass is for the wrong reason, and code can't catch that.
- **Grounded and repeatable are as last measured,** on the drafts and replay of 2026-11-01, before the finder's bundles, D96 and D106. On a scratch copy today, 45 of 50 rebuilt `bundle.json` files are identical and 5 differ (SONB-28, 32, 33, 34, 37; SONB-33 in `stale` and `chars`, cause not traced), and 82 of 90 saved drafts are stale, so a replay would make AI calls until they're made again (about $3.30). `bundle.md` changes with D107's label. Re-confirming both is part of the re-run.

### Decisions made or changed
D69–D109, above all: D79 (bars), D81 (cost), D89–D91 (setup failures), D95, D96 (what a proof means), D100 (held-out set), D101 (the finder replaces the page picker), D104 (the turn cap), D105 (on-fix results), D106 (not reproduced), D107 (the finder's guess), D108 (stale drafts), D109 (closing the phase).

### Known gaps
- **Nothing shows the bundle helps a fix.** Every bar checks a piece. On the finder's 4 misses it covers none of the fix's files, and its gain over the finder comes from 4 tickets.
- **Bug or intended:** only a person can tell (D96). D99 is meant to help, and can't be shown on Sonarr: its planted FSD covers none of the 10 not-bugs.
- **No root cause:** by design (D107). Code describes what a test did, not why (D105).
- **Proof:** only one full run, so its flakiness is unknown; a unit test runs once. SONB-17 was offered only Playwright; SONB-16 passes alone though it reached the bug in its fixture (cause inferred); setup problems count against the AI (SONB-10, 18, 28, 46). Bare test names can't tell two fixtures apart.
- **Graph:** `IExecute<T>` handlers, a hub made by test callers, code dispatched by interface or name, framework-run code.
- **Pull and Jira:** the copy into Jira has no read-back (BWB-3 lost a comment's words); bitwarden's "Build Version" heading isn't read as the version; a sign-in approved with only D82's scopes is unconfirmed.
- **Not checked:** Karma/Jasmine and Mocha test targets; a third repo of another shape; a real FSD (D56).

### Impact on other phases
Phase 4 consumes `ticket-bundle` v2 and `bug-proof` v1; it hasn't started, so no phase goes `needs review`. D99 becomes phase 2c, before Phase 4; it adds a contract Phase 3 consumes, so Phase 3 goes `needs review` when it lands (D20).

### Follow-ups
1. **Phase 2c, D99,** before Phase 4 (D109), starting with its ticket → requirement checker.
2. **The proof re-run on Sonarr** (at most about $45, the $40 cap plus about $4 of drafts; last time's recorded cost was $13, held down by D104's bug; owner's OK; remove `bug-proof.json` first; re-sync the benchmark binary), with the drafts phase and replay after it, to re-confirm grounded and repeatable; then the smaller proof issues if they still matter.
3. **Phase 4's first question:** does the bundle help? The fix agent on the same bugs three ways (Open questions).
4. **Phase 4:** the graph gaps; recording what a failing test ran as evidence; an agent waits for a person's go on a ticket with no proof (D75).

### Needs review 2026-11-04 · `code-graph` v3 → v4 (overhaul 1.1, 1.2; D115)
v4 added `opens-screen` lines, kept apart from the walked lines, and `meta.dirtyFiles`. Follow-up: confirm nothing here depends on v3, re-run this phase's tests, then move `consumes` to `code-graph: 4`.
Checked 2026-11-04 (D20): by grep, `Stangent.Tickets` reads none of the new values; bug proof calls `PlaywrightSpecs.Files(dir)` and `LiteralGotos`, unchanged on a readable tree. All tests pass, so this now consumes `code-graph: 4`.

The code that finds related specs moved from `AppSession` to `PlaywrightSpecs` in `Stangent.Graph`, with the same behaviour; `AppSession.RelatedSpecs` and `RouteMatches` stay, and bug proof's tests pass unchanged.

Checked 2026-11-04 (D20, D117): `source-document` v1 → v2. By grep, `Stangent.Tickets` reads sections through `SpecIndex`, by `id`, `title` and `text`, and never `permanentId` or the document's `schemaVersion`. All tests pass, so this now consumes `source-document: 2`. Its code wasn't touched.

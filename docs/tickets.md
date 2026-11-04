# Tickets (Phase 3: contracts `ticket-bundle`, `bug-proof`, `ticket-update`)

A Jira ticket becomes a checked context bundle for the developer's Claude Code (D69). Code does everything it can;
each AI step gets one input file, returns JSON in a schema, and is checked by code before anything uses it (D18, D60).

```
stangent ticket pull <KEY>          the ticket, comments, attachments           code
stangent ticket points <KEY>        where the work starts                       code → AI picker only if code finds none → you
stangent ticket bundle <KEY>        pages, FSD sections, impact, pointers       code
stangent ticket prove <KEY>         a failing test before any fix (bugs)        AI writes the test → code builds and runs it
stangent ticket draft <KEY> …       questions / resolution comment              AI words it → code checks every claim
stangent ticket post <KEY> <draft>  posts it under your own login               you
```

In Claude Code: `/stangent:ticket <KEY>` runs pull → points → bundle and offers the rest. For batches and CI:
`plugin/scripts/ticket-run.sh [--prove] [--questions] [--resolution <range>] <KEY>...`.

## Jira (D73, D82)

- stangent is an MCP client of Atlassian's MCP server **v2**, `https://mcp.atlassian.com/v2/mcp`, through the official C#
  MCP SDK, calling tools by name from code. v1 has no attachment download.
- `stangent jira login` signs in through the browser (OAuth 2.1, dynamic client registration, redirect to
  `http://127.0.0.1:47813/callback`), asking only for Jira's scopes (`read:jira:agent-interface`,
  `write:jira:agent-interface`, `search:jira:agent-interface`, plus identity and `offline_access`). The token container
  (refresh token and registered client) goes to the OS keychain: service `stangent.jira`, account
  `oauth:mcp.atlassian.com/v2/mcp`. Later runs refresh silently.
- Unattended runs: `stangent jira login --api-token --email <you>` prompts for an API token (or reads it from stdin) and
  keeps it in the keychain; CI sets `STANGENT_JIRA_API_TOKEN` instead. An organization admin must allow API tokens
  (Atlassian Administration → Connectors → Atlassian MCP server → Authentication).
- Secrets never go on a command line, in a file or in an error message, and only to an https Atlassian host:
  `tickets.endpoint` elsewhere is refused (`untrusted_endpoint`).
- `stangent jira tools | call <tool> --args <json> | download <attachment id> --cloud-id <site> --out <file>` are for
  debugging.
- Calls used: `getJiraIssue` (`view: "full"`), `executeRead` → `listJiraIssueComments` (when more than the 20 embedded
  comments) and `downloadJiraIssueAttachment` (a short-lived URL code fetches, https on an Atlassian host only),
  `addOrEditJiraIssueComment`. Nothing else is written: no transitions, no field edits (D74).

## The adapter and replay (D24, D73, D77)

Every step reads and writes through `IIssueTracker`. The Jira backend saves each raw tool result with its hash under
`.stangent/tickets/<KEY>/raw/`; the parser reads only the fields it names, and a missing or changed field fails with
`jira_unexpected_output` instead of quietly emptying the ticket. `tickets.replay: <folder>` (or `adapters.tickets: file`)
reads tickets from saved results in the same layout (`<KEY>/raw/*.json`, `<KEY>/attachments/<id>/<file>`): a replayed
pull gives the same `ticket.json`, byte for byte, as the live one. `tickets.projects` refuses keys from other projects.

## Pull (D72, D75)

`.stangent/tickets/<KEY>/ticket.json` (git-ignored): the ticket's fields, the description and comments (a body Jira
sends as HTML is turned by code into the Markdown it would otherwise have sent: links, images, list numbers, task ticks
and code marks kept, D102), and every attachment saved with its hash. Text files go in up to `tickets.attachmentChars`;
`.docx` goes through 2b's converter, flat if it has no headings, and if it can't be opened it's listed with `notConverted`, so an attachment never stops the pull; images and anything else are listed with where they're mentioned; files over
100 MB are listed, not downloaded. For a bug (`tickets.bugTypes`): steps, expected, actual, version and environment from
the field `tickets.fields` names, Jira's own environment and affects-version fields, or a labelled part of the
description (`### Expected Behavior`, `**Actual:**`); the missing ones are listed.

## Starting points (D101; D70 as the fallback)

- **The file finder** (`tickets.aiPoints`, default on): `stangent-file-finder`, a headless coding agent, gets the
  ticket (summary, description, comments, text attachments) and searches the repo itself, read-only: Read, Grep and Glob
  inside the repo, never `.stangent/` or `.git/` (permission rules the driver passes; an allow rule on the repo bounds
  Grep and Glob too). It returns up to 10 files, most likely first, each with a quote from the ticket, a line copied
  from the file and a one-sentence reason. Code checks each file is in the repo, each quote is in the ticket word for
  word (at least 4 words, or a whole line) and each line is in its file (at least 12 characters, and two words or a whole line, D103); one
  retry with the errors. After it, files that pass are kept and the rest are listed under `ai.rejected`, never dropped
  silently, and shown in the bundle ("Named, not shown in code"). An import or namespace line is no evidence. Capped by
  `tickets.findBudget` (30 turns, $1; a retry gets what's left). The driver runs it in a throwaway git worktree of the
  commit checked out, so it never sees `.stangent/`, untracked files or build output; code checks paths against the
  files git tracks there (exact case) and lines against git's copy. Recorded with its input hash (the instructions and
  the ticket) and the commit it searched: an unchanged ticket makes no AI call, and once the code moves on the answer is
  kept and marked `stale`; `stangent ticket points <KEY> --refind` searches again.
- **Code's signals, when the finder is off** (`tickets.aiPoints: false` or `ai.enabled: false`): stack-trace frames
  (.NET, also async and lambda frames; script frames by file) in the ticket or a text attachment → methods, by file and
  line or by name; URL paths → route pages and endpoint pages; quoted screen names, page titles and FSD section titles
  (through `specs/trace.yaml`) → pages; symbol names that exist in the graph. With the finder on they're only leads in
  the ticket it reads.
- **No starting point at all** (none found, none passed the checks, or all removed): the bundle says "No confident
  starting point", and the questions draft must ask the reporter where it happens (`where` is listed as missing).
- **You:** `--add` / `--remove` a page, a symbol or a file (its path in the repo); replayed on every run and recorded
  with git's `user.name`.
- **Outside the code (D83):** parts whose cause lives where the repo doesn't: `server`, `network`, `deployment`,
  `data`, `third-party`, `other`. The file finder may return up to 3 (`outside`, each quoted from the ticket and
  checked word for word); you add one with `--add-outside "network: times out behind the proxy"` and remove an area's
  with `--remove-outside network` (or `all`). Screens, APIs and app-level security bugs are in the code, never outside.
  With some outside part and no starting point, the bundle says "None in the code" instead of "No confident starting
  point", and `where` isn't asked.

## The bundle (D71, D101)

`bundle.json` (validated against `schemas/ticket-bundle.v2.json`) and `bundle.md`: the ticket and attachments; the
starting points with their reasons (a found file with its ticket quote, its line and the finder's reason, labelled as an
unchecked guess and left out of the reproducer's input, D107); the reference pages that explain
them, each saying which found files it explains (its statements cite code in the file, or the file is in its slice),
after your own and code's page points; the FSD sections linked to the included pages with each link's state and
whether a person decided it (findings only with `fsd.findings`, marked unconfirmed); the impact answer from the
starting points within 2 lines (the found files' code, symbol points, page points' citations); pointers to files and
symbols, the found files first. **No source code.** Over `tickets.bundleChars` (compact JSON): first the impact
answer's item lists go (counts kept, `cap.impactTrimmed`; `stangent graph impact` lists them), then the lowest-ranked
page becomes a pointer (a page point keeps its cited files as pointers; a page that only explains a file doesn't). The
starting points are never cut; a bundle still over with every page cut says so (`cap.over`). Missing and stale pages
are flagged (`/stangent:spec-generate`), never generated here. `finder` records the commit the finder searched and
whether HEAD has moved on since (judged when the bundle is built). Same inputs and the same commit checked out → the
same bytes.

## The bug proof (D75, D76, D81)

- **Targets, by code:** .NET test projects (xUnit, NUnit, MSTest) referencing the projects of the code at the
  starting points, whatever their kind (D89: a page point counts as its entry, so a handler picked as a page is that
  handler; when no unit test covers that code, the code the point pages run, from their slices, is tried next); the
  Jest or Vitest package holding their frontend files; Playwright when `tickets.app` says how to build, start and seed the
  app (`tickets.app.specDir`, default `e2e/stangent`). None: **not provable here**, recorded with no AI call.
- **Outside the code (D83):** with no starting point and some part outside the code, proof is skipped: `not-provable`,
  `outside: true`, no AI call. Remove a wrong part (`ticket points --remove-outside`) and the next run tries. With a code
  point too, the reproducer runs; its `not-provable` is then accepted without the push-back retry, marked `outside`.
- **`stangent-bug-reproducer`** (multi-turn) may edit only in the targets' folders and run only their build and test
  commands (permission rules in `result.next.limits`), within `tickets.proveBudget` (default $1, 30 turns) **per attempt**
  (a turn is a model round, what Claude Code's `--max-turns` limits; the driver counts them, D104):
  a retry runs under what's left, and `check --step prove --usage <result>` sums both calls. In Claude Code,
  `/stangent:ticket` runs it through `ticket-run.sh`, so the rules and caps always apply. Rules use absolute paths and
  each command starts back in the repo (D89), so a `cd` in the agent's shell doesn't change what they allow.
- **Setup failures (D89):** when Claude Code refused something those rules allow (its `permission_denials`: a write they
  allow, or a command line made only of allowed commands and `cd`s) and the test doesn't reproduce the bug, the attempt is `failed` with `setupBlocked: true`: no retry, `check` exits with
  `setup_blocked`, and `ticket-run.sh` stops the batch. The next run tries the ticket again. An app whose start command
  fails (exits non-zero) is reported at once with that command's error, not after `readySeconds`. A test runner that
  can't run at all (D90: a run with no result, and the runner lists no tests for the project) is a setup failure the
  same way (`runner-error`); if it lists tests, the test crashed the host or was misnamed, and the reproducer retries. The start command
  should stay in the foreground: stangent stops the app by stopping it.
- **Code checks:** the test file is in an allowed folder; the named assertion is on its line; the expected-result quote
  is in the ticket word for word; it compiles; and it **fails on that assertion** (TRX, Jest/Vitest and Playwright
  reports read by code; an exception in setup, a timeout or another assertion isn't proof; a mock's own check, such
  as Moq's `Verify`, is an assertion, D90). Playwright runs 3 times.
- **Outcomes** (`bug-proof.json`): `reproduced` (with the other tests already failing at that commit, the baseline),
  `not-reproduced` (the test passes, or the attempt hit its cap; `noProof` says which, D106: `ran-out`, `test-passed`, `flaky`),
  `not-reproduced-flaky`, `not-provable`, `failed` (no
  valid test after one retry). Every written test names the ticket's actual result word for word (`actualQuote`); a
  reproduced one also names the words of its failure that show it (`shows`: in the whole message, not in the test's own
  source). Code checks both are real. `reproduced` shows the code differs from what the reporter expected, not that it's
  a bug (D96): code can't tell a bug from intended behaviour, so it stays a person's call. `--confirm` records the
  developer confirming the failure is the reported problem and a bug (D95, D96); until then the proof is advice, and
  the question writer is told not to call it a bug (an instruction, not a code check). A later attempt clears it. `--hint "…"` makes a new attempt; every attempt is kept.
- **`--verify` on the fix branch (D105):** `verification.result` says what the test did: `passed` (it passes, no test
  outside the baseline fails), `new-failures` (it passes, others started failing), `same-failure` (it fails with the words
  its proof named in `shows`; a proof with no `shows` compares the message without its stack), `different-failure` (it
  fails, those words gone: the reported failure looks gone and something else fails), `not-compiled` (`reason` says whether
  the error is in the test) or `not-run` (a timeout, no test found, a runner error, the test file not on the branch). `testChanged` says the test was edited
  after the proof; `othersChecked` is false when no full run judged the other tests (Playwright has none). Code describes what happened, not why: a person reads anything but a pass, and a pass can still be for the
  wrong reason (SONB-26).

## Drafts and posting (D74)

`stangent ticket draft <KEY> --kind questions|resolution [--tone internal|client] [--pr base..head --pr-url …]`: code
gathers the facts (missing fields, clashes' candidates, the proof; or the PR's files, the pages and FSD sections it
reaches, the proof); `stangent-question-writer` / `stangent-summary-writer` returns the comment as parts
(`draft-output` v2, D98): each part is one sentence or line with the ids of the claims it makes, and each claim is linked.
Code checks every link, quotes word for word, that each part holds one sentence, that a questions draft asks for every
missing field and mentions a bug that wasn't reproduced when a test really ran (an attempt that only ran out can't be linked, D106), and that a `client` draft has no backticks, paths, ids or code
names, and that each claim backs one part. A part that states something with no claim is left out, not a failed draft
(D97); a pure ask (a question or "Please …" with no reason or fact in it), a greeting that is nothing else and a short
heading or lead-in need none. Code joins the kept parts into Markdown. Recorded in `drafts/<kind>-<tone>.json`
(`ticket-update` v1: `dropped` lists what was left out, `unchecked` what was kept with no claim) and `.md` (under "Left
out" and "Kept with no claim"). `stangent ticket post <KEY> <kind>[-<tone>]` posts the text
under your own login and records who and when; never twice without `--again`, and not if what the draft was made from has
changed or can't be worked out (`draft_stale`, which gives the command to make it again) without `--stale` (D108). A resolution
compares with its PR's range, so pass fixed commits: a relative range moves with a later commit.

## Settings (`stangent.yaml`)

```yaml
tickets:
  site: acme.atlassian.net          # or the cloudId
  endpoint: https://mcp.atlassian.com/v2/mcp
  auth: oauth                       # or apiToken (with email)
  email: you@acme.com
  projects: [PROJ]
  bugTypes: [Bug]
  fields: { steps: Steps to Reproduce, expected: Expected Result, actual: Actual Result }
  tone: internal                    # or client
  bundleChars: 40000
  attachmentChars: 8000
  aiPoints: true
  textFiles: [src/App/Localization/en.json]   # UI texts: a string in the code that is a key here reads as its text (D87)
  replay: benchmark/tickets/x/raw   # read saved results instead of Jira
  app: { build: …, start: …, url: http://localhost:8989, seed: …, readySeconds: 120, specDir: e2e/stangent, playwrightDir: e2e }
  proveBudget: { usd: 1, turns: 30 }
  dotnetArgs: ""                    # added to the proof's dotnet build/test; SolutionDir, NuGetAudit and Windows targeting come from the repo unless set here (D94)
  rank: { summaryWeight: 2, phraseBoost: 2, depthDecay: 0.35, winnerRatio: 1.5, lengthNorm: 0.1 }   # page ranking weights (D87, D94); unset keeps these
```

Playwright runs with `.stangent.playwright.config.ts`, which stangent writes beside the repo's own config
(`playwrightDir`) on every run: the repo's config as it is, with the base URL set to `app.url`, `specDir` reachable,
and the repo's `webServer` off, since stangent starts the app (D94). `app.start` should stay in the foreground.

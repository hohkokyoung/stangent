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

`.stangent/tickets/<KEY>/ticket.json` (git-ignored): the ticket's fields, the description and comments (HTML bodies
converted to text by code), and every attachment saved with its hash. Text files go in up to `tickets.attachmentChars`;
`.docx` goes through 2b's converter; images and anything else are listed with where they're mentioned; files over
100 MB are listed, not downloaded. For a bug (`tickets.bugTypes`): steps, expected, actual, version and environment from
the field `tickets.fields` names, Jira's own environment and affects-version fields, or a labelled part of the
description (`### Expected Behavior`, `**Actual:**`); the missing ones are listed.

## Starting points (D70)

- **Code:** stack-trace frames (.NET, also async and lambda frames; script frames by file) in the ticket or a text
  attachment → methods, by file and line or by name; URL paths → route pages and endpoint pages; quoted screen names,
  page titles and FSD section titles (through `specs/trace.yaml`) → pages; symbol names that exist in the graph.
- **AI, only when code finds none** (`tickets.aiPoints`, default on): `stangent-point-picker` sees the ticket and the
  page titles only, and returns up to 5 pages, each with a quote; code checks each page exists and each quote is in the
  ticket word for word; one retry. Recorded with its input hash: an unchanged ticket makes no AI call.
- **You:** `--add` / `--remove` a page or symbol; replayed on every run and recorded with git's `user.name`.

## The bundle (D71)

`bundle.json` (validated against `schemas/ticket-bundle.v1.json`) and `bundle.md`: the ticket and attachments; the
starting points with their reasons; the chosen pages' statements with citations (your own additions rank first); the
FSD sections linked to them with each link's state and whether a person decided it (findings only with
`fsd.findings`, marked unconfirmed); the impact answer from the pages' cited symbols and the matched symbols within 2
lines; pointers to files and symbols. **No source code.** Over `tickets.bundleChars` (compact JSON) the lowest-ranked
page becomes a pointer. Missing and stale pages are flagged (`/stangent:spec-generate`), never generated here. Same
inputs → the same bytes.

## The bug proof (D75, D76, D81)

- **Targets, by code:** .NET test projects (xUnit, NUnit, MSTest) referencing the starting points' projects; the Jest
  or Vitest package holding their frontend files; Playwright when `tickets.app` says how to build, start and seed the
  app (`tickets.app.specDir`, default `e2e/stangent`). None: **not provable here**, recorded with no AI call.
- **`stangent-bug-reproducer`** (multi-turn) may edit only in the targets' folders and run only their build and test
  commands (permission rules in `result.next.limits`), within `tickets.proveBudget` (default $1, 15 turns) **per attempt**:
  a retry runs under what's left, and `check --step prove --usage <result>` sums both calls. In Claude Code,
  `/stangent:ticket` runs it through `ticket-run.sh`, so the rules and caps always apply.
- **Code checks:** the test file is in an allowed folder; the named assertion is on its line; the expected-result quote
  is in the ticket word for word; it compiles; and it **fails on that assertion** (TRX, Jest/Vitest and Playwright
  reports read by code; an exception in setup, a timeout or another assertion isn't proof). Playwright runs 3 times.
- **Outcomes** (`bug-proof.json`): `reproduced` (with the other tests already failing at that commit, the baseline),
  `not-reproduced` (the test passes, or the attempt hit its cap), `not-reproduced-flaky`, `not-provable`, `failed` (no
  valid test after one retry). `--hint "…"` makes a new attempt; every attempt is kept. `--confirm` records the developer
  confirming the failure matches the reported actual result. `--verify` on the fix branch: the test passes and no test
  outside the baseline fails.

## Drafts and posting (D74)

`stangent ticket draft <KEY> --kind questions|resolution [--tone internal|client] [--pr base..head --pr-url …]`: code
gathers the facts (missing fields, clashes' candidates, the proof; or the PR's files, the pages and FSD sections it
reaches, the proof); `stangent-question-writer` / `stangent-summary-writer` marks every claim `[Cn]` and links it. Code
checks every link, quotes word for word, that a questions draft asks for every missing field and mentions a bug that
wasn't reproduced, and that a `client` draft has no backticks, paths, ids or code names. Recorded in
`drafts/<kind>-<tone>.json` (`ticket-update` v1) and `.md`. `stangent ticket post <KEY> <kind>[-<tone>]` posts the text
under your own login and records who and when; never twice without `--again`.

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
  app: { build: …, start: …, url: http://localhost:8989, seed: …, readySeconds: 120, specDir: e2e/stangent }
  proveBudget: { usd: 1, turns: 15 }
```

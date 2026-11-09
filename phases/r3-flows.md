---
phase: r3
name: flows
status: in progress
consumes: { code-graph: 5 }
provides: { flows: 1, areas: 1 }
---

# Rebuild layer 3: areas → flows (D154, D156, D170–D180)

Designed with the owner on 2026-11-09, one question at a time. Nothing built yet.

## Bottom line

A **flow** is one thing a person or the system does, laid out by code from the graph: who starts it, what they
enter, what's checked, the steps with their conditions, where each branch ends, and the flows it leads to.
An **area** is a group of flows, made by a fixed folder rule and confirmed once by a person. **Layer 3 uses no
AI.** It starts with a return to layer 2, because the graph doesn't yet know what runs when a screen opens, what
a job is, what queues a job, or the conditions around a call.

## The design, by decision

| | Decision |
|---|---|
| Order | flows first, areas grouped from them afterwards; a screen change ends a flow and links to that screen's opening flow (D170) |
| Starts | a click or submit (one flow per handler), a screen opening, a system job; event handlers are linked, not starts (D171) |
| Ends | per branch: the database, an outside call, a message sent (handlers linked through `triggered-by`, never through the bus), another screen, a hub (10+ callers), a ❓, 8 real calls (D172); early exits (D174) |
| Links | every out-of-bounds end is a link to another flow, explained by a code-made sentence with its proof line; jobs marked "runs later" (D173) |
| Conditions | each step carries the condition it sits under, quoted from the code; validation rules are a checks step (D174) |
| Labels | a URL, form or generated-client match is ✅ when constant and unique; other guesses 🟡 per step with their reason, listed at the top (D175) |
| Determinism | source-line order then id; loops "go back to step N"; names from translation files; every React effect on open; a fixed early-exit list; a display-only rule; byte-for-byte rebuilds (D176) |
| Contents | name, start, actor, inputs, checks, steps, ends, scenarios (main path plus one per early exit), links, guesses, settings (D179) |
| Areas | the screen's home folder; shared folders never a home; jobs to the area that links to them most; split over 40 flows; never merged by code; ties listed for a person (D178) |
| Disk | `specs/flows/<id>.json` + `.md`, `specs/flows/index.json`, `specs/areas.yaml`, all committed; contracts `flows` v1, `areas` v1 (D180) |

Worked example (Sonarr, D172–D173): **Add {title}** (signed-in user clicks) → `useAddSeries` → `POST /api/v5/Series` →
`SeriesController.AddSeries` → `AddSeriesService` → `SeriesService.AddSeries` → saves `Series`, sends SeriesAdded →
**SeriesAdded handler** queues `RefreshSeriesCommand` (runs later) → **Refresh series** (system) asks SkyHook, saves `Episodes`.

## Build order

Each step is code only unless it says otherwise. The cost of every agent is said before it starts, and the
assistant stops and asks before a second round (D158).

1. **Layer 2 additions** (row r2, report section 17), each written for any stack, with fixtures on React, Angular and Razor:
   - **runs on load:** React effects (every effect runs on first render; with dependencies, "runs again when X changes"), Angular `ngOnInit` and route resolvers, an MVC GET action returning a view (D171, D176);
   - **is a job:** command handlers (`IExecute<T>`), scheduled tasks (`IScheduleTask`), recurring-job registrations (Hangfire `RecurringJob`) (D171);
   - **queues:** a queued command's type → the handler of that type; a background-job lambda → its method; a type made at runtime → ❓ (D173);
   - **conditions and early exits:** the innermost `if`/`else`/`switch` case/`catch`/loop around each call, quoted, with the outer ones; returns of `BadRequest`, `NotFound`, `Unauthorized`, `Forbid`, `Challenge`, `Redirect…`, `StatusCode(4xx/5xx)`, and `throw` (D174, D176);
   - **✅ URL rule:** constant verb, constant path (route values excepted), constant API root, exactly one endpoint, else 🟡 naming the failed test (D175).
   Graphs rebuilt on Sonarr, Kavita and nopCommerce; counts before and after. Then a **narrow fresh check (D158)** of these
   additions only, estimated at 60–100K tokens.
2. **The frozen key (D177):** a fresh agent reads only the source of the three repos and writes the 15 flows' steps, ends
   and conditions, each with its code line, into `benchmark/expected/flows/`. Estimated at 100–150K tokens. Committed before
   step 3; the owner approves three (one per repo).
3. **Flows:** `stangent flows build|show|check`, contract `flows` v1, `docs/flows.md`; built on the three repos; scored against
   the key by code (a flows scorer in `Stangent.Benchmark`). The bar below decides.
4. **A narrow fresh check (D158) of the flows**, estimate said first.
5. **Areas:** `stangent areas propose|check`, contract `areas` v1; the owner confirms Sonarr's `areas.yaml` (the others proposed and listed).
6. **Then D166:** the guesses and blind spots that sit on a flow, and only those, are sorted by cause and checked. Then layer 4 (pages, P1).

## The bar (D177), frozen 2026-11-09

The 15 starts:

| | Sonarr | Kavita | nopCommerce |
|---|---|---|---|
| Click / submit | Add series; Delete series; save an indexer | the Want to Read star; Login | Register; Add to cart; Confirm order |
| Screen opens | Series details | Series detail; the manga reader | the product page |
| System job | Refresh series | the scan-libraries recurring job (`ScanLibrariesTaskId`) | `QueuedMessagesSendTask` |

1. **No wrong ✅:** a certain step the code doesn't do fails the bar outright.
2. **At least 95%** of the key's steps, ends and conditions found; a key step counts only if it quotes a code line.
3. **Every 🟡 has a reason and every ❓ says why:** 100%, checked by code.
4. **Byte for byte:** two builds from the same commit and settings are identical, on all three repos.
5. **No silent skips:** every start code finds in a whole repo becomes a flow or a stated reason.

When Stangent and the key disagree, the quoted code line decides, and the ruling is written here.

### Rulings (step 3)

Each is brought to the owner one at a time, with a recommendation; none is applied until the owner rules.

- **R1, ruled 2026-11-09 (owner: "Yes, count it", on the recommendation; D185): the key stops a branch one call earlier than
  the code does.** The key ends many branches at a service or repository call (`_episodeService.GetEpisodeBySeries(series.Id)` →
  `database`), or at a call it didn't follow (`unknown`, "not walked"), where the code touches the table one or more calls down.
  The code line decides: such a key end counts as found when Stangent's step on that line leads, through its own steps (and
  `sameAs`), to an end of the same kind (`database`, `outside-call`, `message`); a key `unknown`/`depth` counts when Stangent walks
  on from that step. The key isn't edited. Effect on item 2: Sonarr 62% → 66%, Kavita 69% → 78%, nopCommerce 77% → 84%.
- **R2, ruled 2026-11-09 (owner: "Yes, leave it", on the recommendation): Kavita's `unitOfWork.CommitAsync` stays `hub`.**
  Its body saves through the database context and names no table on that line, so Stangent ends it `hub` (289 callers), as the
  key does; the key's detail "writes AppUserWantToRead" isn't scored. No change to the key or the scorer.
- **R3, ruled 2026-11-09 (owner: "Yes, keep it", on the recommendation): confirm-order is the multi-step checkout.** The key
  follows `CheckoutController.ConfirmOrder`; the one-page `OpcConfirmOrder` is its own flow by D171, built but not scored. No
  change to the key or the scorer.
- **R4, open (listed at step 2): `return InvokeHttp404()`** (2 product-page exits): not on D176's fixed list, so not an exit.
- **R5, open (listed at step 2): access filters setting `context.Result`** (Kavita `EntityAccessAttribute` ×4, nopCommerce
  `CheckAccessPublicStore` ×2, `ValidateHoneypot`): Stangent now walks the filters as steps (D184) but, per D181, an assignment
  to `context.Result` isn't an exit.
- **R6, open: calls into packages** (ASP.NET Identity's `userManager`, MailKit's `client.ConnectAsync`/`smtpClient.SendAsync`,
  RxJS subjects): the graph keeps only the repo's own points, so these are no steps. A layer 2 gap, not a key error.
- **R7, open: the key walks into code the graph counts as a hub** (≥10 callers by distinct calling methods, through interfaces),
  and lists steps inside it; D172 stops there.

## Step 2: the key — done 2026-11-09 (15 flows, three approved, one per repo)

- Format: `benchmark/expected/flows/README.md` (D183). Every counted item is `at` + exact `code`.
- **Sonarr** (commit `0bffefe7c8`), written 2026-11-09 by a fresh Sonnet agent from git-tracked source only (~230K tokens):

  | Flow | Steps | Ends | Conditions | Exits | Links | Checks |
  |---|---|---|---|---|---|---|
  | add-series | 16 | 6 | 13 | 6 | 5 | 10 |
  | delete-series | 7 | 3 | 3 | 1 | 15 | 1 |
  | save-indexer | 30 | 13 | 18 | 5 | 12 | 14 |
  | series-details-opens | 35 | 12 | 11 | 0 | 0 | 0 |
  | refresh-series | 38 | 24 | 40 | 3 | 13 | 0 |

  Checked by code: 306 items, every `at` a tracked line whose text holds its `code`. Spot-read by hand: add-series's
  controller → add service → insert → `SeriesAddedEvent` (3 handlers) → `RefreshSeriesCommand` queued. **Owner approved `add-series` (2026-11-09, "Approve add-series")**: Sonarr's approved key (D177).
  The agent's own caveats: V5 controllers only (the frontend's root is `/api/v5`); delete's options act in event
  handlers, so they sit as extra `conditions` on two links; save-indexer's connection test stops at the depth limit and
  only Newznab's settings rules are listed; series-details leaves four `GET /series` mappers unwalked.
- **Kavita** (commit `2d225367f0`), written 2026-11-09 by a fresh Sonnet agent from git-tracked source only (~260K tokens):

  | Flow | Steps | Ends | Conditions | Exits | Links | Checks |
  |---|---|---|---|---|---|---|
  | want-to-read | 13 | 5 | 19 | 5 | 2 | 0 |
  | login | 21 | 13 | 38 | 5 | 2 | 2 |
  | series-detail-opens | 115 | 37 | 44 | 18 | 2 | 0 |
  | manga-reader-opens | 74 | 23 | 48 | 18 | 3 | 0 |
  | scan-libraries-job | 5 | 4 | 6 | 0 | 3 | 0 |

  Checked by code: 453 items; 2 quotes of `auth.guard.ts:16` (an empty line) moved to `:17`, where the quoted `if` is,
  before commit; then 0 problems. Spot-read by hand: want-to-read's toggle (add or remove), both endpoints, their
  `Unauthorized`/`NotFound`/`BadRequest` exits and the Hangfire scrobble queued after the commit hold. **Owner approved `want-to-read` (2026-11-09, "Approve want-to-read")**: Kavita's approved key (D177).
  Open for a ruling when scored: saves through `unitOfWork.CommitAsync` (289 callers) are ended `hub` "writes
  `AppUserWantToRead`", where D172's order puts `database` before `hub`. The scan job stops where it queues
  `ScannerService.ScanLibraries` (a `runs-later` link, D173), so the scan's own work isn't in the key. Agent's caveats:
  route guards and resolvers are first steps of the screen flows; login's `apiKey` path, series-detail's SignalR and
  fragment subscriptions and the reader's settings auto-save aren't walked; Identity calls end as `database`.
- **nopCommerce** (commit `77d112f365`), written 2026-11-09 by a fresh Sonnet agent from git-tracked source only (~250K tokens):

  | Flow | Steps | Ends | Conditions | Exits | Links | Checks |
  |---|---|---|---|---|---|---|
  | register | 56 | 46 | 134 | 11 | 12 | 39 |
  | add-to-cart | 45 | 34 | 50 | 1 | 15 | 10 |
  | confirm-order | 63 | 42 | 134 | 23 | 15 | 11 |
  | product-page-opens | 29 | 25 | 24 | 7 | 4 | 0 |
  | queued-messages-send-task | 17 | 12 | 13 | 1 | 0 | 0 |

  Checked by code: 724 items, 0 problems. Spot-read by hand: the email task's two loops, SMTP send, `catch` that logs
  and `finally` that saves `SentTries` hold. **Owner approved `queued-messages-send-task` (2026-11-09, "Approve the email job")**: nopCommerce's approved key (D177).
  Open for a ruling when scored: (a) confirm-order follows the multi-step `CheckoutController.ConfirmOrder`, whose first
  exit sends the shopper to one-page checkout when `OnePageCheckoutEnabled` (the installer sets it true,
  `InstallRequiredData.cs:1870`); the one-page `OpcConfirmOrder` is its own flow by D171 and isn't in the key.
  (b) 2 product-page exits are `return InvokeHttp404()`, an app helper that sets 404, not on D176's fixed list.
  (c) 2 exits are access filters setting `context.Result` (register's redirect, add-to-cart's `Challenge`), not results
  the action returns (D181). Agent's caveats: add-to-cart's redirects are JSON values the page script follows (`screen`
  ends, not exits); terms-of-service is a page-script check; repository-level entity-event consumers aren't listed;
  model-factory sub-builders, `PrepareAndValidate*`, OAuth helpers and payment plugins end `unknown`.

## Step 3: the flow code — built 2026-11-09, scored; the bar is not passed

`stangent flows build|show|check` (contract `flows` v1, `docs/flows.md`, D184), a flows scorer in `Stangent.Benchmark`
(`--score-flows <clone> <key-dir>`), built on the three clones at the key's commits. Layer 2 grew for it (D184): `call-site`
facts (each call's line), a hook's own functions as points, `filterType` on access filters; graphs rebuilt. Builds take ~4 s.

| Bar item | Sonarr | Kavita | nopCommerce |
|---|---|---|---|
| Flows built (click / screen / job / handler) | 367 | 281 | 944 |
| 1. No wrong ✅ (code check: a ✅ call's line names what it calls) | 51 flagged of 48,840 | 5 of 23,707 | 10 of 75,196 |
| 2. Key steps, ends and conditions found (≥95%), after R1 | **187 / 284 = 66%** | **398 / 511 = 78%** | **646 / 767 = 84%** |
| — before R1 | 175 / 284 = 62% | 354 / 511 = 69% | 590 / 767 = 77% |
| — steps | 91 / 126 | 207 / 228 | 187 / 210 |
| — ends (incl. exits), after R1 | 38 / 73 | 82 / 128 | 149 / 202 |
| — conditions | 58 / 85 | 109 / 155 | 310 / 355 |
| (links, not in item 2) | 28 / 45 | 4 / 12 | 16 / 46 |
| (checks, not in item 2) | 8 / 25 | 0 / 2 | 26 / 60 |
| 3. Every 🟡 and ❓ with a reason | pass (10,680 🟡, 705 ❓) | pass (1,182, 119) | pass (7,271, 269) |
| 4. Byte for byte (two builds, and disk) | pass | pass | pass |
| 5. No silent skips (starts → flows or stated reasons) | pass (2,678 starts) | pass (1,305) | pass (1,244) |

**Bottom line: items 3–5 pass on all three; item 2 fails (66%, 78%, 84% against 95%, after R1); item 1 isn't settled** — the samples read
are the checker's limits (a hook's function returned under another name, `goToPage: handleGoToPage`; a chain over 10 lines) plus
one extractor quirk (Kavita's route resolvers' `on-load` lines point at a blank line). Steps are mostly found (72%, 91%, 89%);
ends are what fail. Where the misses sit: the key stopping a branch one call earlier than the code does (R1, ruled: counted);
filters' `context.Result` and app helpers not on D176's exit list (R4, R5); calls into packages and navigation, which the graph
doesn't hold (R6; `screen` ends: none built); a URL built at runtime (Sonarr's save-indexer stops at a ❓ before the server, 3 of
30 steps); the key walking into hubs (R7). Per-flow numbers: `--score-flows` output (`flows[].items`, `flows[].missed`).

## Left open, on purpose

- How many links along a chain the FSD judge sees: layer 5 (D173).
- W2, W3, V6–V10 from layer 2: fixed when a flow in the trial needs them (D169).
- Wording smoother than the templates: layer 4 (P1), checked by code (D173).

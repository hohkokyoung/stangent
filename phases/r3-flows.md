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
  **Tightened 2026-11-09 (owner: "sure", after a review found it too loose; D185 amended):** a key `database` end counts through
  Stangent's steps below only when they reach a table the key names (read through the graph's entity→table map, since the key
  names entities); a key `unknown`/`depth` that only says the key didn't follow the call ("not walked", "same … as", a depth
  stop) leaves the count where Stangent walks on (`endsLeftOut`), instead of counting as found. 44 such ends left out (Sonarr 5,
  Kavita 3, nopCommerce 36); 6 credits lost that reach another table (1, 4, 1). Item 2: Sonarr 66% → 64.9%, Kavita 84% →
  82.7%, nopCommerce 88% → 87.1% (on the flows built before D189).
- **R2, ruled 2026-11-09 (owner: "Yes, leave it", on the recommendation): Kavita's `unitOfWork.CommitAsync` stays `hub`.**
  Its body saves through the database context and names no table on that line, so Stangent ends it `hub` (289 callers), as the
  key does; the key's detail "writes AppUserWantToRead" isn't scored. No change to the key or the scorer.
- **R3, ruled 2026-11-09 (owner: "Yes, keep it", on the recommendation): confirm-order is the multi-step checkout.** The key
  follows `CheckoutController.ConfirmOrder`; the one-page `OpcConfirmOrder` is its own flow by D171, built but not scored. No
  change to the key or the scorer.
- **R4, ruled 2026-11-09 (owner: "Yes, make it an exit", on the recommendation; D186): `return InvokeHttp404()` is an early
  exit.** Its body sets `Response.StatusCode = 404` and returns, so the code ends the request there. D176's list grows by one
  general rule: a returned call to an app's own helper that sets a constant 4xx/5xx status, or returns only listed results, is an
  exit. Built in layer 2 (18 such exits on nopCommerce); the key's 2 product-page exits and their 2 conditions are now found
  (nopCommerce item 2: 84.2% → 84.7%). No change to the key.
- **R5, ruled 2026-11-09 (owner: "Yes, an exit", on the recommendation; D187): a filter setting `context.Result` to a refusal is
  an early exit**, changing D181. Setting it stops the request before the action runs. Built in layer 2; flows already walk the
  filters first (D184). The key's 7 such exits (Kavita `EntityAccessAttribute` ×4, nopCommerce `CheckAccessPublicStore` ×2,
  `ValidateHoneypot`) and their conditions are now found: Kavita 77.9% → 80.6%, nopCommerce 84.7% → 85.9%. No change to the key.
- **R6, ruled 2026-11-09 (owner: "Yes, show them", on the recommendation; D188): a call into a package is a step, not
  walked.** C# calls into packages (not `System.*`, EF Core or logging) are `call-site` facts with no target (Sonarr 24,852,
  Kavita 8,908, nopCommerce 12,940); a flow shows each as "calls X in the package Y", ending `unknown`, "a package's code, not
  walked". TypeScript's aren't recorded; SMTP as an `outside-call` is a separate question. Kavita 80.6% → 83.6%, nopCommerce
  85.9% → 87.9%. No change to the key.
- **R8, ruled 2026-11-10 (owner: "Leave it out", on the recommendation; D194): a key item quoting no code leaves the count.**
  Kavita's login-guard exit (both screen flows) quotes `auth.guard.ts:25` `};`, the line after the redirect on line 24. The
  scorer leaves any key step or exit whose quote holds no letter or digit out, with its conditions (2 exits, 2 conditions).
- **R9, ruled 2026-11-10 (owner: "Yes, extend R1", on the recommendation; D194): R1 credits `screen` ends.** Navigation
  through the app's own helper (`navService.handleLogin()`, `closeReader()`) ends one call below the key's end; found when
  Stangent's steps below reach a `screen` end (2 items). Kavita 91.9% → 93.0%.
- **R10, ruled 2026-11-10 (owner: "Yes, count it", on the recommendation; D195): an exit's condition counts when it's on a
  step on the way to the exit.** Sonarr's `OnActionExecuting` wraps `ValidateResource(…)` (whose `throw` is the exit) in an
  `if` and a loop; Stangent records them on that call's step. Sonarr 85.8% → 89.5%, nopCommerce 91.4% → 92.2%.
- **R11, ruled 2026-11-10 (owner: "Yes, count it", on the recommendation; D197): a key `hub` end counts on a step that runs
  that hub and ends `database`.** The same shared code; Stangent names the table it reads first (D172). 11 items: Sonarr
  92.4% → 93.8%, Kavita 93.0% → 93.6%, nopCommerce 92.5% → 93.0%.
- **R7, withdrawn 2026-11-09: a Stangent bug, fixed by D189.** The key walks into code the graph counts as a hub and lists steps
  inside it; Stangent stopped there with a `continues` link to a shared walk D173 promised but never built. Now each hub and
  depth stop a flow continues into is a `shared` flow, and the scorer follows the links into them (D189); a field or property
  read (an Angular signal) is no longer a step or a hub. Recount, by item: 25 key items now found in shared code (Sonarr 4 steps,
  4 ends; Kavita 3 steps, 4 ends, 2 conditions; nopCommerce 7 ends, 1 condition), 3 more "not walked" ends left out (Kavita 1,
  nopCommerce 2), none lost. Item 2: Sonarr 64.9% → 67.7%, Kavita 82.7% → 84.4%, nopCommerce 87.1% → 88.2%. Most of R7's
  ~40 items by line were not separate misses: what's left in shared code is a key `hub` where Stangent ends `database` (the hub
  reads a table, D172's order: Sonarr 4, Kavita 3, nopCommerce 4), and nopCommerce reads written as LINQ over a repository's
  `Table` (`_customerRepository.Table.Where(…)`), which layer 2 records as no read, so the shared flow stops at
  `FirstOrDefaultAsync` (a hub) instead of `Customer` (about 5 ends).

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

## Step 3: the flow code — built 2026-11-09, scored; the bar is not passed (rescored after D185's amendment, D189–D193, D196, D198–D200 and rulings R8–R11)

`stangent flows build|show|check` (contract `flows` v1, `docs/flows.md`, D184), a flows scorer in `Stangent.Benchmark`
(`--score-flows <clone> <key-dir>`), built on the three clones at the key's commits. Layer 2 grew for it (D184): `call-site`
facts (each call's line), a hook's own functions as points, `filterType` on access filters; graphs rebuilt. Builds take ~4 s; with shared flows (D189) 6 s on Sonarr, 12 s on nopCommerce.

| Bar item | Sonarr | Kavita | nopCommerce |
|---|---|---|---|
| Flows built (click / screen / job / handler / shared) | 1,660 (116 / 31 / 38 / 182 / 1,293) | 558 (233 / 27 / 23 / 0 / 275) | 1,392 (325 / 399 / 17 / 203 / 448) |
| 1. No wrong ✅ (code check: a ✅ call's line names what it calls) | 71 flagged of 89,757 | 5 of 31,994 | 8 of 92,457 |
| 2. Key steps, ends and conditions found (≥95%), after R1 (tightened), R4–R6, R8–R11, D189–D193, D196, D198–D200 | **262 / 275 = 95.3% ✓** | **481 / 503 = 95.6% ✓** | **696 / 729 = 95.5% ✓** |
| — before D200 (ASP.NET Identity's store) | 262 / 275 = 95.3% | 473 / 503 = 94.0% | 696 / 729 = 95.5% |
| — before D199 (query reads, mail clients, MVC results, self-calls; Sonarr and Kavita unchanged by it) | 262 / 275 = 95.3% | 473 / 503 = 94.0% | 678 / 729 = 93.0% |
| — before D198 (outside calls with runtime URLs, else-if guards) | 258 / 275 = 93.8% | 471 / 503 = 93.6% | 678 / 729 = 93.0% |
| — before R11 | 254 / 275 = 92.4% | 468 / 503 = 93.0% | 674 / 729 = 92.5% |
| — before D196 (table-reading hubs continue into shared flows) | 246 / 275 = 89.5% | 468 / 503 = 93.0% | 672 / 729 = 92.2% |
| — before R10 | 236 / 275 = 85.8% | 468 / 503 = 93.0% | 666 / 729 = 91.4% |
| — before R8, R9 | 236 / 275 = 85.8% | 466 / 507 = 91.9% | 666 / 729 = 91.4% |
| — before D193 (requests made another way; Sonarr and nopCommerce unchanged by it) | 236 / 275 = 85.8% | 453 / 507 = 89.4% | 666 / 729 = 91.4% |
| — before D192 (navigation, error callbacks) | 236 / 275 = 85.8% | 428 / 507 = 84.4% | 664 / 729 = 91.1% |
| — before D191 (a view's onclick into its script; Sonarr and Kavita unchanged by it) | 236 / 275 = 85.8% | 428 / 507 = 84.4% | 643 / 729 = 88.2% |
| — before D190 (URLs callers give; Kavita and nopCommerce unchanged by it) | 189 / 279 = 67.7% | 428 / 507 = 84.4% | 643 / 729 = 88.2% |
| — before D189 (R1 tightened, old flows) | 181 / 279 = 64.9% | 420 / 508 = 82.7% | 637 / 731 = 87.1% |
| — before R1 was tightened | 187 / 284 = 66% | 427 / 511 = 84% | 674 / 767 = 88% |
| — before any ruling | 175 / 284 = 62% | 354 / 511 = 69% | 590 / 767 = 77% |
| — steps | 126 / 126 | 223 / 228 | 204 / 210 |
| — ends (incl. exits) | 53 / 64 | 111 / 122 | 144 / 164 |
| — conditions | 83 / 85 | 147 / 153 | 348 / 355 |
| (key ends left out: they only say the key didn't follow a call, R1) | 9 | 4 | 38 |
| (links, not in item 2) | 36 / 45 | 4 / 12 | 17 / 46 |
| (checks, not in item 2) | 11 / 25 | 0 / 2 | 26 / 60 |
| 3. Every 🟡 and ❓ with a reason | pass (21,088 🟡, 1,091 ❓) | pass (1,471, 156) | pass (10,565, 332) |
| 4. Byte for byte (two builds, and disk) | pass | pass | pass |
| 5. No silent skips (starts → flows or stated reasons) | pass (2,678 starts) | pass (1,305) | pass (1,244) |

**Bottom line: items 2–5 pass on all three (item 2: Sonarr 95.3%, Kavita 95.6%, nopCommerce 95.5%, after R1 tightened,
R4–R6, R8–R11, D189–D193, D196 and D198–D200); item 1 isn't settled, which the step 4 fresh check is for** (more ✅ steps to check now that shared flows are written: Sonarr's flags rose 51 → 71) — the samples read
are the checker's limits (a hook's function returned under another name, `goToPage: handleGoToPage`; a chain over 10 lines) plus
one extractor quirk (Kavita's route resolvers' `on-load` lines point at a blank line). Steps are mostly found (72%, 91%, 89%);
ends are what fail. Where the misses sit: the key stopping a branch one call earlier than the code does (R1, ruled: counted);
filter and helper exits (ruled exits, R4, R5); navigation, which the graph doesn't hold (`screen` ends: none built; package calls now steps, R6); a URL built at runtime (Sonarr's save-indexer stops at a ❓ before the server, 3 of
30 steps, now followed by D190: save-indexer's steps 30 of 30); inside shared code, a key `hub` where the hub reads a table (Stangent ends `database`, D172) and nopCommerce's LINQ
reads over a repository's `Table`, which layer 2 doesn't record (R7's recount). Per-flow numbers: `--score-flows` output (`flows[].items`, `flows[].missed`).

## Step 4: the narrow fresh check — run 2026-11-10 (one fresh agent, ~194K tokens against a 100–150K estimate)

**Bottom line: item 1 fails.** ~145 ✅ steps checked against the code (Sonarr 47, Kavita 46, nopCommerce 52): 0 wrong on
Sonarr and nopCommerce, 3 wrong on Kavita, each from a general cause (confirmed by reading the code):

1. **A function passed as an argument is walked as if called there.** `action-factory.service.ts:75`
   `(action, entity) => this.actionService.handleSeriesAction(action, entity),` is a menu callback; the series-detail screen
   flow shows ✅ `DELETE /api/series/{*}` and other menu actions as happening on open (321 of its 1,416 steps). Same shape:
   login's `connection.on(event, (resp) => …)`.
2. **Any `src =` that isn't a URL is read as a GET** (D193): `this.canvasImage.src = '';` (manga reader step 92) is ✅
   "HTTP GET to a URL built at runtime".
3. **A parent route's resolver on a lazy child route gets the child file with the parent's line:** step 48 at
   `manga-reader.router.module.ts:91` (a 22-line file); the resolver is at `app-routing.module.ts:91`.

The tool's own flags read: Sonarr `goToPage(1)` and nopCommerce `ForeignKey` (statement's first line) are the checker's
limits; Kavita step 48 is real (3 above). All 12 rules added in step 3 (D189–D200) hold, with: rule 5 too wide (cause 2);
D198 misses a plain `HttpClient.GetAsync` with a runtime URL (ends `unknown`); self-call loops also come from `base.` calls.
Other general findings (no wrong ✅): guards inside a `lock` block don't reach code after it; `base.X()` dispatched to every
override (false loops, Mono code calling Windows code); a generic controller's type not narrowing dispatch (save-indexer
fans out to 32 `Test` implementations, 🟡); `shared-eventaggregator-publishevent` walks all 156 handlers (3,724 steps,
against "the bus is never walked"); an overload 🟡 reason used for two same-named JS globals; HTTP step text with no path
("calls *"); `location.href = …` missing its `if`; Kavita repository reads listed twice on one line. Full report: the
session's notes folder, `step4-fresh-check.md`. Not fixed yet: waits for the owner.

## Left open, on purpose

- How many links along a chain the FSD judge sees: layer 5 (D173).
- W2, W3, V6–V10 from layer 2: fixed when a flow in the trial needs them (D169).
- Wording smoother than the templates: layer 4 (P1), checked by code (D173).

---
phase: r3
name: flows
status: planned
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

## Left open, on purpose

- How many links along a chain the FSD judge sees: layer 5 (D173).
- W2, W3, V6–V10 from layer 2: fixed when a flow in the trial needs them (D169).
- Wording smoother than the templates: layer 4 (P1), checked by code (D173).

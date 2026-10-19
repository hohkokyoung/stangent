---
phase: 1b
name: frontend-api
status: done
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 2 }
provides: {}
---

# Phase 1b · Frontend + API shapes

**Approved 2026-10-19. Done 2026-10-19.**

Builds on D17, D26, D27, D30–D32. The goal: impact answers that cross from the C# backend into
Angular and React frontends, and back, with every cross-stack link measured against a real answer
key. No AI anywhere (D18).

This phase **extends 1a's contracts** rather than providing new ones. When it lands it reopens
`phases/1a-csharp-map.md` and bumps `code-graph` to v2 (new point and line kinds) and
`impact-report` to v3 (a `pages` group), per D20.

## What gets built

### 1. The TypeScript extractor (`tools/ts-extractor/`, Node; D30)
- Written in TypeScript, bundled with esbuild into one file shipped inside the `stangent` dotnet
  tool. It bundles **TypeScript 6.0.3** (the JavaScript compiler API) and `@angular/compiler`.
  - **Risk, checked first:** TypeScript 7 is the new native compiler. If its API is stable by build
    time, revisit; until then 6.0.3 parses the benchmark repos' TypeScript 5.9 and 6.0.
- **Input:** a frontend project (its `tsconfig`). **Output:** a fragment on stdout in the
  `code-graph` JSON-lines format (points, lines, blind spots, facts), which C# saves like any other
  fragment.
- **Points:**
  - ids look like `ts:<project>|<file>#<name>`, e.g. `ts:Web|src/app/_services/series.service.ts#SeriesService.getSeries`;
  - files, classes and interfaces, functions and methods, properties;
  - Angular components and React components;
  - **pages** (routes).
- **✅ lines from the compiler:** calls, uses, inherits/implements, and **renders** (a component
  using a child component, through JSX).
- **Angular templates:** parsed with Angular's own parser. Selectors link to components (✅ when
  unique, 🟡 when several match); bindings link to the members they use. NgModule and standalone apps both work.
- **Pages:**
  - Angular Router: `{ path, component | loadComponent | loadChildren }`;
  - React Router: `<Route path element>` and `createBrowserRouter([...])`;
  - each page routes to its component.
- **❓ blind spots:** dynamic imports with computed paths, URLs built at runtime, `any`-typed responses.
- Its own tests run with `node --test`.

### 2. Running it from `stangent graph build` (C#)
- **Finds frontend projects:** a `tsconfig.json` (or `tsconfig.app.json`) beside a `package.json`,
  outside `node_modules`.
- **Fingerprint:** a frontend's fingerprint covers its source, template, config and lock files;
  incremental builds re-run only changed frontends.
- **Restore:** `npm ci` / `yarn install --frozen-lockfile` / `pnpm install --frozen-lockfile`
  (chosen by lock file) when `node_modules` is missing; `--no-restore` skips it.
  - Without packages, frontend calls fall back to 🟡 name matching, and `loadWarnings` says so.
- **Node missing** → a clear `node_missing` error for frontends only; the C# graph still builds.

### 3. Linking (C# detectors, link step only)
- **`frontend-http`**: turns frontend call facts into links to backend endpoints, reusing
  `http-services`' path scoring (🟡).
  - **Angular `HttpClient`, `fetch` and axios:** verb and URL from the code. Base URL prefixes such as
    `environment.apiUrl` or `this.baseUrl` are an unknown prefix, matched against the end of each endpoint's path.
  - **In-house wrappers**, named in `detectorSettings: { frontend-http: { wrappers: [...] } }`. Sonarr's
    `useApiQuery({ path })` is the benchmark case.
  - **Generated clients:**
    - read the generated file when it exists (its URLs become ordinary calls);
    - otherwise **NSwag naming rules**: `TodoListsClient.getTodoLists()` → the `GetTodoLists` action in the `TodoLists` group or controller.
- **API data fields:**
  - **Backend:** each endpoint's request and response types become their JSON field names, using
    ASP.NET Core's default camelCase, `[JsonPropertyName]` / `[JsonProperty]`, and naming policies set in code where detectable.
  - **Frontend:** a TypeScript interface used as a typed call's result or body (`get<Series[]>`,
    `useApiQuery<Series[]>`) gets each property linked to the matching C# property (**maps-field**, 🟡).
  - Nested types are followed 3 levels deep.
  - The payoff: renaming a backend property reaches the frontend components that read that field.

### 4. Impact answers
- A **`pages`** group (frontend routes), listed nearest first like the others.
- Frontend code joins `code`, `tests` (`*.spec.ts` / `*.test.tsx` cases) and `projects`.

### 5. Contracts
- **`code-graph` v2:**
  - point kinds: `page`, `component`;
  - line kinds: `renders`, `maps-field`;
  - the `ts:` id scheme.
  - Documented in `docs/code-graph.md`.
- **`impact-report` v3:** the `pages` group (`schemas/impact-report.v3.json`).

### 6. Benchmark (D6, D28, D32)
- Kavita and Sonarr join the harness. Sonarr uses its SDK override.
- **Test 4 for the frontend:** rename sampled TypeScript members and component inputs, then type-check
  (`tsc --noEmit`; for Angular, the project's own template type-check), score against the files that fail.
- **OpenAPI check (Kavita):**
  - our endpoints vs `openapi.json` paths and verbs;
  - our C# JSON field names vs its schemas;
  - linked frontend calls → endpoints that exist in it.
- **History tests 1–2** on Kavita and Sonarr, now including frontend files.
- **CleanArchitecture:** `benchmark/expected/cleanarchitecture-links.json`, a short list of
  frontend → endpoint links, drafted by the agent and checked by a person once.

### 7. Fixture and CI
- `tests/fixtures/shop` gains a small Angular frontend and a small React frontend. Tiny committed type
  stubs for `@angular/common/http` and `react` keep the tests offline and fast.
- CI adds `actions/setup-node` and runs the extractor's own tests.

## Done when
- [x] `stangent graph build` on Kavita, Sonarr and CleanArchitecture includes their frontends; a
      missing Node or missing packages are reported, not fatal to the C# graph.
- [x] Frontend builds are deterministic, and an incremental build after a one-file frontend change equals a full build
      (fixture test; the history runs' Test 3 also covers the benchmark repos, see below).
- [x] **Kavita:** impact on a series endpoint's backend action reaches the Angular service method that
      calls it and the pages that show it.
- [x] **Sonarr:** impact on a backend series endpoint reaches the `useApiQuery` call site and its page.
- [x] **CleanArchitecture:** impact on the `TodoLists` endpoint reaches `Todo.jsx` (React) and the Angular
      component through the NSwag naming rules.
- [x] Renaming a backend DTO property (impact from the property) reaches the frontend code that reads that field.
- [x] Frontend Test 4, the OpenAPI check and history tests run; results in the report.
- [x] Tests (C# and Node) cover the extractor, templates, pages, `frontend-http`, NSwag rules and field mapping.
- [x] CI green (with Node) ([run](https://github.com/hohkokyoung/stangent/actions/runs/39213514381)); end-of-phase report written.

## Not in this phase
- Vue and Svelte templates; generators other than NSwag (each is a small rule set when a repo needs it).
- Frontends in separate repos (D4).
- 📜 git history (1c).

## End-of-phase report

*2026-10-19 · first report.* CI green on GitHub ([run](https://github.com/hohkokyoung/stangent/actions/runs/39213514381)); phase **done**.

### 1. What was built
- **`tools/ts-extractor/`** (TypeScript, bundled with esbuild into one file, shipped in the dotnet
  tool with TypeScript's standard-library type files). It bundles TypeScript 6.0.3 and
  `@angular/compiler` 22.2.3. It produces:
  - points for files, classes, functions, members, components, templates, pages and tests;
  - ✅ lines from the type checker, including `renders` (JSX), and contextual properties (object
    literals, JSX attributes, destructuring) linking to the interface fields they fill;
  - Angular templates parsed with Angular's own parser;
  - Angular Router and React Router pages, including lazy and index routes and wrapped elements;
  - HTTP call facts (Angular `HttpClient`, `fetch`, axios, configured wrappers), generated-client
    facts, and data shapes.
- **C#:**
  - `FrontendScanner` finds, fingerprints, restores and extracts frontends:
    - restore uses `npm ci`, falling back to an install that leaves the lock file alone;
    - yarn and pnpm run through `npx` at their pinned version; install scripts are never run;
    - a missing Node or an empty extractor output is a load error.
  - `frontend-http` links calls to endpoints (path scoring, `{base}` prefixes, NSwag naming) and
    fields to properties (`maps-field`).
  - `ApiShapes` records endpoint request/response types and JSON field names.
- **Routes detector, fixed on real repos:** inherited route attributes, custom `IRouteTemplateProvider`
  attributes, minimal-API wrappers bound by parameter, `groupConventions`, Windows-only projects loadable on macOS/Linux.
- **Contracts:** `code-graph` v2 and `impact-report` v3 (`pages` group); 1a reopened and bumped per D20.
- **Benchmark:**
  - frontend Test 4 (`tsc` compile-break), the OpenAPI check, and the
    `benchmark/expected/cleanarchitecture-links.json` answer key (drafted by the agent from the backend
    code, **not yet checked by a person**);
  - history tests now count frontend files;
  - a run restores files left edited by an interrupted run.
- **Tests:** 55 C# (incl. Angular and React fixture apps with committed type stubs) and 7 Node; CI now sets up Node.

### 2. Benchmark results (`benchmark/results/2026-10-19-phase1b-*.json`)
**Graphs**
| | Kavita (Angular) | Sonarr (React) | CleanArchitecture (Angular + React) |
|---|---|---|---|
| C# projects / frontends | 15 / 1 | 28 / 1 | 13 / 2 |
| Endpoints · pages · components | 522 · 51 · 303 | 364 · 37 · 648 | 10 · 11 · 22 |
| Frontend calls → endpoints | 445 | 264 | 21 (NSwag naming) |
| `maps-field` links | 1,400 | 1,021 | 0 (generated client, no types) |
| Unmatched frontend calls (❓) | 6 | 23 | 6 (ASP.NET Identity endpoints, not in the repo) |
| Graph build | ~30 s | ~37–50 s | ~10 s |

**Answer keys (D32)**
| Check | Result |
|---|---|
| Kavita endpoints vs `openapi.json` (522 operations) | **recall 1.00, precision 1.00** |
| Kavita JSON field names vs `openapi.json` (247 endpoints) | **recall 1.00, precision 1.00** |
| Kavita frontend links landing on a real endpoint | **445 of 445**; 387 of 405 callers resolve to exactly one endpoint |
| CleanArchitecture answer key (28 generated-client calls) | **22 right, 6 correctly ❓, 0 wrong, 0 missing** |

**Frontend Test 4: rename a TypeScript member, `tsc --noEmit`** (40 renames each; CleanArchitecture Angular 20)
| Predictor | Kavita (42 broken files) | Sonarr (256) | CleanArchitecture Angular (4) |
|---|---|---|---|
| graph ✅, direct (depth ≤ 1) | **0.88 · 0.97** | 0.36 · 0.94 | 1.00 · 1.00 |
| graph ✅, depth ≤ 2 | 0.98 · 0.55 | 0.68 · 0.44 | 1.00 · 0.20 |
| graph ✅, unlimited | 0.98 · 0.09 | 1.00 · 0.07 | 1.00 · 0.09 |
| same folder | 0.05 · 0.002 | 0.06 · 0.03 | 0.00 · 0.00 |

(recall · precision)

How to read it:
- **Backend ↔ frontend links are exact where an answer key exists.** Endpoints, field names and
  generated-client calls scored 1.00 or 22/22 against OpenAPI and the CleanArchitecture key.
- **TypeScript code links are good on Angular, weaker on React.** Sonarr's direct recall is 0.36
  because of **structural typing**: renaming a prop breaks every place that passes *any* object of the
  right shape, even code that never names it. A reference map can't see that; a type check can. Within
  2 lines recall is 0.68, overall 1.00, so the links exist, just further out.
- Angular templates aren't type-checked by `tsc`, so Test 4 doesn't score template links (they're
  covered by the fixture tests). CleanArchitecture's React app is `.jsx`: no types, nothing to score.

**History tests (`*-history.json`)**: changed C# methods as the starting point; frontend files count as targets.

| Test | Predictor | Kavita | Sonarr |
|---|---|---|---|
| **Backend change → the frontend files the commit also touched** | graph | **0.26** · 0.015 (105 cases) | **0.47** · 0.002 (87 cases) |
| | same folder | 0.00 · 0.00 | 0.00 · 0.00 |
| | co-change | 0.01 · 0.04 | 0.01 · 0.04 |
| **Test 2, predicts what broke** | graph, unlimited | 0.29 · 0.010 (24 pairs) | 0.94 · 0.002 (17 pairs) |
| | graph, depth ≤ 2 | 0.02 · 0.003 | **0.74 · 0.027** |
| | same folder | 0.12 · 0.006 | 0.38 · 0.011 |
| | co-change | **0.32 · 0.037** | 0.21 · 0.074 |
| **Test 3** | identical builds · incremental = full | ✅ · ✅ (24.5 s full, 14.8 s incremental) | ✅ · ✅ (23.3 s full; **5.4 s after a `.tsx` change, 1 frontend re-extracted**) |

(recall · precision)

- **Only the graph crosses the stack.** Folder and co-change baselines find essentially none of the
  frontend files a backend change goes with. The graph's answers are wide, though (median 45 files on
  Kavita, 1,150 unlimited on Sonarr), so the distance ranking matters.
- **"What broke" is split, and the samples are small.** On Kavita, co-change (history) beats the
  graph; on Sonarr, the graph within 2 lines wins. That's direct evidence that 📜 history evidence
  (Phase 1c) adds something the graph doesn't, and that neither alone is enough.

### 3. Decisions made or changed
- **TypeScript 6.0.3 is bundled.** TypeScript 7's API is still `unstable/*` only.
- **Restore never runs install scripts** (arbitrary code); a lock file out of step with
  `package.json` falls back to an install that doesn't touch the lock.
- **Wrapper URLs and base-URL prefixes are matched against the end of endpoint paths** (`{base}`).
- **Angular components depend on their own members;** a page routes to every component its element renders.
- **Benchmark repos needing SDK overrides:** CleanArchitecture and Sonarr (10.0.402), like nopCommerce.
- **In-house conventions live in config:** `groupConventions` (CleanArchitecture), `wrappers` (Sonarr).

### 4. Known gaps
- **Structural typing** (above): TypeScript assignability breakage isn't a reference, so it's found only at 2+ lines or not at all.
- **Angular template type information:** template expressions like `order?.total` link to `order`, not to `Order.total`.
- **Framework-provided endpoints** (ASP.NET Identity's `MapIdentityApi`) aren't in the repo, so calls to them are ❓.
- **Generators other than NSwag;** Vue and Svelte.
- **The Angular compile-break test misses template breakage:** it would need `ngc`.
- **Bugs found and fixed along the way, each now covered by a test or check:**
  - the shipped bundle silently overwritten by a stale file (now always copied; empty output is an error);
  - TypeScript's lib files missing from the bundle;
  - wrapped route elements;
  - contextual properties.
- Not tested on Windows.

### 5. Impact on other phases
- `code-graph` v1 → v2 and `impact-report` v2 → v3. Both are provided by 1a and reopened there (D20); 1b consumes them.
- Phase 2 (spec) and later can link requirements to pages and frontend components, not only endpoints.

### 6. Follow-ups
- ~~Push and confirm CI is green, then mark this phase `done` here and in `roadmap.md`.~~ Done.
- A person checks `benchmark/expected/cleanarchitecture-links.json` once.
- 1c (git history) or Phase 2 next. The owner decides. Kavita's Test 2 (co-change beat the graph) argues for 1c.

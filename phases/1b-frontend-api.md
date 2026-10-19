---
phase: 1b
name: frontend-api
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 1 }
provides: {}
---

# Phase 1b · Frontend + API shapes

**Approved 2026-10-19. In progress.**

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
- [ ] `stangent graph build` on Kavita, Sonarr and CleanArchitecture includes their frontends; a
      missing Node or missing packages are reported, not fatal to the C# graph.
- [ ] Frontend builds are deterministic, and an incremental build after a one-file frontend change equals a full build.
- [ ] **Kavita:** impact on a series endpoint's backend action reaches the Angular service method that
      calls it and the pages that show it.
- [ ] **Sonarr:** impact on a backend series endpoint reaches the `useApiQuery` call site and its page.
- [ ] **CleanArchitecture:** impact on the `TodoLists` endpoint reaches `Todo.jsx` (React) and the Angular
      component through the NSwag naming rules.
- [ ] Renaming a backend DTO property (impact from the property) reaches the frontend code that reads that field.
- [ ] Frontend Test 4, the OpenAPI check and history tests run; results in the report.
- [ ] Tests (C# and Node) cover the extractor, templates, pages, `frontend-http`, NSwag rules and field mapping.
- [ ] CI green (with Node); end-of-phase report written.

## Not in this phase
- Vue and Svelte templates; generators other than NSwag (each is a small rule set when a repo needs it).
- Frontends in separate repos (D4).
- 📜 git history (1c).

## End-of-phase report
_Not started._

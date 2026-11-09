import { test } from "node:test";
import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { extract } from "../src/extract.ts";
import type { Line } from "../src/model.ts";

function project(files: Record<string, string>, compilerOptions: Record<string, unknown> = {}): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "stangent-flow-"));
  for (const [name, text] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(root, name)), { recursive: true });
    fs.writeFileSync(path.join(root, name), text);
  }
  fs.writeFileSync(path.join(root, "package.json"), '{ "name": "t" }');
  fs.writeFileSync(path.join(root, "tsconfig.json"), JSON.stringify({ compilerOptions: { target: "ES2022", module: "ES2022", moduleResolution: "bundler", jsx: "preserve", ...compilerOptions }, include: ["src"] }));
  return root;
}

const run = (root: string, settings: Record<string, Record<string, string[]>> = {}) =>
  extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings }).lines;
type Fact = Extract<Line, { t: "fact" }>;
const facts = (lines: Line[], kind: string) => lines.filter((l): l is Fact => l.t === "fact" && l.kind === kind);
const after = (id: string) => id.split("#")[1];

const REACT = "declare function useEffect(f: () => void, deps?: unknown[]): void; declare function useLayoutEffect(f: () => void, deps?: unknown[]): void;\n"
  + "declare namespace JSX { interface IntrinsicElements { [name: string]: unknown } }\n";

test("every React effect runs on first render; its function is a point; dependencies say when it runs again (D176)", () => {
  const root = project({
    "src/api.ts": "export function fetchSeries(id: number) {} export function fetchTags() {} export function track() {}\nexport function useSeries(id: number) { return id; }",
    "src/Details.tsx": REACT + [
      "import { fetchSeries, fetchTags, track, useSeries } from './api';",
      "export function Details(p: { seriesId: number }) {",
      "  const s = useSeries(p.seriesId);",
      "  useEffect(() => { fetchSeries(p.seriesId); }, [p.seriesId]);",
      "  useEffect(() => { fetchTags(); }, []);",
      "  useLayoutEffect(() => { track(); });",
      "  return <div>{s}</div>;",
      "}",
    ].join("\n"),
  });
  const lines = run(root);
  const loads = facts(lines, "on-load").map((f) => `${f.data.hook} ${after(f.data.handler)} [${f.data.deps}] ${f.data.runs}`);
  assert.deepEqual(loads, [
    "useSeries useSeries [] first render, then every render",
    "useEffect Details.effect1 [p.seriesId] first render, then again when a dependency changes",
    "useEffect Details.effect2 [] first render",
    "useLayoutEffect Details.effect3 [] first render, then after every render",
  ]);
  // The effect's calls are its own, and the component uses the effect, so impact still reaches it.
  assert.ok(lines.some((l) => l.t === "edge" && l.kind === "calls" && l.source.endsWith("#Details.effect1") && l.target.endsWith("#fetchSeries")));
  assert.ok(lines.some((l) => l.t === "edge" && l.kind === "uses" && l.source.endsWith("#Details") && l.target.endsWith("#Details.effect2")));
});

test("an effect outside a component's own body, or with dependencies that aren't a list, is unknown, not guessed", () => {
  const root = project({
    "src/X.tsx": REACT + [
      "declare const deps: unknown[];",
      "export function X(p: { on: boolean }) {",
      "  if (p.on) { useEffect(() => {}, []); }",
      "  useEffect(() => {}, deps);",
      "  return <div />;",
      "}",
    ].join("\n"),
  });
  const blind = run(root).filter((l) => l.t === "blind" && l.detector === "on-load").map((l) => (l as { reason: string }).reason);
  assert.deepEqual(blind, [
    "useEffect not written directly in a component's or hook's body: when it runs isn't read",
    "useEffect dependencies not written as a list: deps",
  ]);
});

test("Angular's ngOnInit, a class component's componentDidMount and a route's resolver run as the screen opens", () => {
  const root = project({
    "src/stub.d.ts": "declare module '@angular/core' { export function Component(m: object): ClassDecorator; }\ndeclare module 'react' { export class Component<P = {}> { props: P } }",
    "src/series.resolver.ts": "export class SeriesResolver { resolve() { return 1; } }\nexport function tagsResolver() { return 2; }",
    "src/series.component.ts": "import { Component } from '@angular/core';\n@Component({ selector: 'app-series', template: '<p></p>' })\nexport class SeriesComponent { ngOnInit() {} }",
    "src/routes.ts": "import { SeriesComponent } from './series.component';\nimport { SeriesResolver, tagsResolver } from './series.resolver';\n"
      + "export const routes = [{ path: 'series', resolve: { series: SeriesResolver }, children: [{ path: ':id', component: SeriesComponent, resolve: { tags: tagsResolver } }] }];",
    "src/Old.tsx": "import * as React from 'react';\nexport class Old extends React.Component { componentDidMount() {} render() { return null; } }",
  }, { experimentalDecorators: true });
  const loads = facts(run(root), "on-load").map((f) => `${f.node.split("|").pop()?.split("#").pop()} ${f.data.hook} ${after(f.data.handler)} ${f.data.runs}`).sort();
  assert.deepEqual(loads, [
    "/series/:id resolve SeriesResolver.resolve before the screen opens",
    "/series/:id resolve tagsResolver before the screen opens",
    "Old componentDidMount Old.componentDidMount first render",
    "SeriesComponent ngOnInit SeriesComponent.ngOnInit first render",
  ]);
});

test("a call's conditions are quoted innermost first; a throw is an exit with its conditions (D174)", () => {
  const root = project({
    "src/save.ts": [
      "export function save(x: number) {}",
      "export function run(items: number[], mode: string, ok: boolean) {",
      "  for (const i of items) {",
      "    if (ok && i > 0) { save(i); } else { switch (mode) { case 'a': save(-i); break; default: throw new Error('bad'); } }",
      "  }",
      "  try { save(0); } catch (e) { save(1); }",
      "}",
    ].join("\n"),
  });
  const lines = run(root);
  const conds = facts(lines, "condition").map((f) => `${f.data.call}: ${JSON.parse(f.data.conditions).map((c: { kind: string; text: string; case?: string }) => `${c.kind} ${c.text}${c.case ? ` = ${c.case}` : ""}`).join(" < ")}`);
  assert.deepEqual(conds, [
    "save: if ok && i > 0 < loop for (const i of items)",
    "save: case mode = 'a' < else ok && i > 0 < loop for (const i of items)",
    "Error: case mode = default < else ok && i > 0 < loop for (const i of items)",
    "save: catch e",
  ]);
  const exits = facts(lines, "exit");
  assert.equal(exits.length, 1);
  assert.equal(exits[0].data.text, "throw new Error('bad');");
  assert.equal(exits[0].data.exception, "Error");
});

test("calls say where their verb came from, and a const object's property is a constant (D175)", () => {
  const root = project({
    "src/environment.ts": "const host = 'localhost';\nexport const environment = { apiUrl: 'http://' + host + ':5000/api/' };",
    "src/http.ts": [
      "import { environment } from './environment';",
      "declare function useApiQuery(o: { path: string }): void; declare function send(verb: string, url: string): void;",
      "declare const verb: string;",
      "export class S { baseUrl = environment.apiUrl; a() { fetch(this.baseUrl + 'series'); } }",
      "export function b() { useApiQuery({ path: '/series' }); }",
      "export function c() { send('PATCH', '/x'); send(verb, '/y'); }",
      "export function d() { fetch('/z', { method: 'POST' }); }",
    ].join("\n"),
  });
  const calls = facts(run(root, { "frontend-http": { wrappers: ["useApiQuery:path", "send:#1:#0"] } }), "call").map((f) => `${f.data.verb} ${f.data.url} ${f.data.verbFrom}`);
  assert.deepEqual(calls, [
    "GET http://localhost:5000/api/series library",
    "GET {base}series name",
    "PATCH {base}x literal",
    "GET {base}y name",
    "POST /z literal",
  ]);
});

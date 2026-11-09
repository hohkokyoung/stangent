import { test } from "node:test";
import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import ts from "typescript";
import { extract, qualifiedName, testName } from "../src/extract.ts";
import { BASE, stringValue } from "../src/values.ts";

/** A throwaway project with these files and a tsconfig including them. */
function project(files: Record<string, string>, compilerOptions: Record<string, unknown> = {}): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "stangent-ts-"));
  for (const [name, text] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(root, name)), { recursive: true });
    fs.writeFileSync(path.join(root, name), text);
  }
  fs.writeFileSync(path.join(root, "package.json"), '{ "name": "t" }');
  fs.writeFileSync(path.join(root, "tsconfig.json"), JSON.stringify({ compilerOptions: { target: "ES2022", module: "ES2022", moduleResolution: "bundler", jsx: "preserve", ...compilerOptions }, include: ["src"] }));
  return root;
}

function program(text: string) {
  const sf = ts.createSourceFile("/x.ts", text, ts.ScriptTarget.ES2022, true);
  const host = ts.createCompilerHost({});
  host.getSourceFile = (f) => (f === "/x.ts" ? sf : undefined);
  host.fileExists = (f) => f === "/x.ts";
  const p = ts.createProgram(["/x.ts"], { noLib: true }, host);
  return { sf, checker: p.getTypeChecker() };
}

test("string values follow constants, template literals and concatenation", () => {
  const { sf, checker } = program(`
    const root = "api/";
    const id = Math.random();
    const a = root + "series/" + id;
    const b = \`\${root}series/\${id}/pic\`;
    declare const environment: { apiUrl: string };
    const c = environment.apiUrl + "account";`);
  const init = (name: string) => {
    let found: ts.Expression | undefined;
    sf.forEachChild(function visit(n): void {
      if (ts.isVariableDeclaration(n) && ts.isIdentifier(n.name) && n.name.text === name) found = n.initializer;
      n.forEachChild(visit);
    });
    return found;
  };
  assert.equal(stringValue(init("a"), checker), "api/series/{*}");
  assert.equal(stringValue(init("b"), checker), "api/series/{*}/pic");
  assert.equal(stringValue(init("c"), checker), `${BASE}account`);
});

test("test names include their describe blocks", () => {
  const sf = ts.createSourceFile("/t.ts", `describe("Orders", () => { describe("place", () => { it("keeps the total", () => {}); }); });`, ts.ScriptTarget.ES2022, true);
  let name: string | null = null;
  sf.forEachChild(function visit(n): void { name ??= testName(n); n.forEachChild(visit); });
  assert.equal(name, "Orders › place › keeps the total");
});

test("qualified names cover classes, members, interfaces and exported functions", () => {
  const sf = ts.createSourceFile("/q.ts", `export class A { m() {} p = 1; } export interface I { x: number } export function f() {} type T = { y: string };`, ts.ScriptTarget.ES2022, true);
  const names: string[] = [];
  sf.forEachChild(function visit(n): void { const q = qualifiedName(n); if (q) names.push(q); n.forEachChild(visit); });
  assert.deepEqual(names.sort(), ["A", "A.m", "A.p", "I", "I.x", "T", "T.y", "f"]);
});

test("a React app gives pages, renders lines and fetch facts; output is deterministic", () => {
  const root = project({
    "src/jsx.d.ts": "declare namespace JSX { interface IntrinsicElements { [name: string]: unknown } }",
    "src/Item.tsx": "export function Item({ id }: { id: number }) { const load = () => fetch(`/api/items/${id}`); return <b onClick={load}>{id}</b>; }",
    "src/App.tsx": "import { Item } from './Item';\ndeclare const Route: any; declare const Routes: any;\nexport function App() { return <Routes><Route path=\"/items/:id\" element={<Item id={1} />} /></Routes>; }",
  });
  const run = () => extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "project:tsconfig.json", settings: {} }).lines;
  const lines = run();
  assert.deepEqual(run(), lines, "two runs give the same lines");
  assert.ok(lines.some((l) => l.t === "node" && l.kind === "page" && l.name === "/items/:id"));
  assert.ok(lines.some((l) => l.t === "edge" && l.kind === "renders" && l.target.endsWith("#Item")));
  const call = lines.find((l) => l.t === "fact" && l.kind === "call");
  assert.ok(call && call.t === "fact");
  assert.equal(call.data.url, "/api/items/{*}");
  assert.equal(call.data.verb, "GET");
});

test("object literals, JSX attributes and destructuring link to the interface fields they fill", () => {
  const root = project({
    "src/jsx.d.ts": "declare namespace JSX { interface IntrinsicElements { [name: string]: unknown } }",
    "src/types.ts": "export interface Req { roles: string[] }\nexport interface CellProps { errorMessage: string }",
    "src/Cell.tsx": "import { CellProps } from './types';\nexport function Cell(props: CellProps) { return <b>{props.errorMessage}</b>; }",
    "src/use.tsx": "import { Req } from './types';\nimport { Cell } from './Cell';\n"
      + "export function save(send: (r: Req) => void) { send({ roles: ['a'] }); }\n"
      + "export function Page() { return <Cell errorMessage=\"x\" />; }\n"
      + "export function read(r: Req) { const { roles } = r; return roles; }",
  });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines;
  const uses = (source: string, target: string) => lines.some((l) => l.t === "edge" && l.kind === "uses" && l.source.endsWith(source) && l.target.endsWith(target));
  assert.ok(uses("#save", "#Req.roles"), "object literal property");
  assert.ok(uses("#Page", "#CellProps.errorMessage"), "JSX attribute");
  assert.ok(uses("#read", "#Req.roles"), "destructuring");
});

test("an Angular component depends on its own members, so member changes reach its page", () => {
  const root = project({
    "src/stub.d.ts": "declare module '@angular/core' { export function Component(m: object): ClassDecorator; }",
    "src/list.component.ts": "import { Component } from '@angular/core';\n@Component({ selector: 'app-list', template: '<p>{{ title }}</p>' })\nexport class ListComponent { title = 'x'; ngOnInit() {} }",
    "src/routes.ts": "import { ListComponent } from './list.component';\nexport const routes = [{ path: 'list', component: ListComponent }];",
  }, { experimentalDecorators: true });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines;
  assert.ok(lines.some((l) => l.t === "edge" && l.kind === "uses" && l.source.endsWith("#ListComponent") && l.target.endsWith("#ListComponent.ngOnInit")));
  assert.ok(lines.some((l) => l.t === "edge" && l.kind === "routes-to" && l.target.endsWith("#ListComponent")));
});

test("a page routes to every component its element renders, and index routes are pages", () => {
  const root = project({
    "src/jsx.d.ts": "declare namespace JSX { interface IntrinsicElements { [name: string]: unknown } }",
    "src/c.tsx": "export function Guard(p: { children?: unknown }) { return <div>{p.children}</div>; }\nexport function Tasks() { return <p />; }\nexport function Home() { return <p />; }",
    "src/routes.tsx": "import { Guard, Tasks, Home } from './c';\nexport const AppRoutes = [{ index: true, element: <Home /> }, { path: '/todo', element: <Guard><Tasks /></Guard> }];",
  });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "project:x", settings: {} }).lines;
  const routes = (page: string) => lines.filter((l) => l.t === "edge" && l.kind === "routes-to" && l.source.endsWith(`|${page}`)).map((l) => (l as { target: string }).target.split("#")[1]).sort();
  assert.deepEqual(routes("/todo"), ["Guard", "Tasks"]);
  assert.deepEqual(routes("/"), ["Home"]);
});

test("configured wrappers: a fixed verb, the verb read off the name, and URL and verb taken from arguments", () => {
  const root = project({
    "src/api.ts": [
      "declare function apiDelete(url: string): void; declare function send(verb: string, url: string): void; declare function updateItem(url: string): void;",
      "export function a() { apiDelete('/items/1'); }",
      "export function b() { send('PATCH', '/items/2'); }",
      "export function c() { updateItem('/items/3'); }",
    ].join("\n"),
  });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p",
    settings: { "frontend-http": { wrappers: ["apiDelete", "send:#1:#0", "updateItem@PUT"] } } }).lines;
  const calls = lines.filter((l) => l.t === "fact" && l.kind === "call").map((l) => l.t === "fact" ? `${l.data.verb} ${l.data.url}` : "").sort();
  assert.deepEqual(calls, [`DELETE ${BASE}items/1`, `PATCH ${BASE}items/2`, `PUT ${BASE}items/3`]);
});

test("a wrapper's own body shows the API root it adds (G2)", () => {
  const root = project({
    "src/fetch.ts": "declare const window: { App: { urlBase: string } };\nexport const urlBase = window.App.urlBase;\nexport const apiRoot = '/api/v5';",
    "src/path.ts": "import { apiRoot, urlBase } from './fetch';\nconst getQueryPath = (path: string) => {\n  return urlBase + apiRoot + path;\n};\nexport default getQueryPath;",
    "src/hooks.ts": [
      "import getQueryPath from './path';",
      "declare function fetchJson(o: { path: string }): void;",
      "export function useApiMutation(options: { path: string; method: string }) { fetchJson({ path: getQueryPath(options.path) }); }",
      "export function useApiQuery(o: { path: string }) { const { path } = o; fetchJson({ path: getQueryPath(path) }); }",
      "export function useRaw(o: { path: string }) { fetchJson({ path: o.path }); }",
    ].join("\n"),
    "src/use.ts": "import { useApiMutation, useApiQuery, useRaw } from './hooks';\nexport function a() { useApiMutation({ path: '/series', method: 'POST' }); useApiQuery({ path: '/series/lookup' }); useRaw({ path: '/raw' }); }",
  });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p",
    settings: { "frontend-http": { wrappers: ["useApiMutation:path:method", "useApiQuery:path", "useRaw:path"] } } }).lines;
  const calls = lines.filter((l) => l.t === "fact" && l.kind === "call").map((l) => l.t === "fact" ? `${l.data.verb} ${l.data.url}` : "").sort();
  assert.deepEqual(calls, [`GET ${BASE}/api/v5/series/lookup`, `GET ${BASE}raw`, `POST ${BASE}/api/v5/series`]);
});

test("a verb is read off whole words of a wrapper's name, never part of one", () => {
  const root = project({
    "src/api.ts": [
      "declare function dispatchQuery(url: string): void; declare function fetchUpdates(url: string): void; declare function useApiMutation(o: { path: string }): void;",
      "export function a() { dispatchQuery('/a'); fetchUpdates('/b'); useApiMutation({ path: '/c' }); }",
    ].join("\n"),
  });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p",
    settings: { "frontend-http": { wrappers: ["dispatchQuery", "fetchUpdates", "useApiMutation:path"] } } }).lines;
  const calls = lines.filter((l) => l.t === "fact" && l.kind === "call").map((l) => l.t === "fact" ? `${l.data.verb} ${l.data.url}` : "").sort();
  assert.deepEqual(calls, [`GET ${BASE}a`, `GET ${BASE}b`, `POST ${BASE}c`]);
});

test("enums are printed only when asked for, with members, values and lines", () => {
  const root = project({ "src/status.ts": 'export enum Status {\n  Open,\n  Closed = 5,\n  Named = "n",\n}\nexport class A {}\n' });
  const options = { tsconfig: path.join(root, "tsconfig.json"), root, projectId: "project:tsconfig.json", settings: {} };
  assert.deepEqual(extract(options).lines.filter((l) => l.t === "enum"), []);
  const found = extract({ ...options, enums: true }).lines.filter((l) => l.t === "enum");
  assert.equal(found.length, 1);
  const status = found[0] as Extract<typeof found[0], { t: "enum" }>;
  assert.equal(status.id, "ts|src/status.ts#Status");
  assert.equal(status.name, "Status");
  assert.deepEqual(status.members, [{ name: "Open", value: "0", line: 2 }, { name: "Closed", value: "5", line: 3 }, { name: "Named", value: "n", line: 4 }]);
  // The rest of the output is what it always was.
  assert.deepEqual(extract({ ...options, enums: true }).lines.filter((l) => l.t !== "enum"), extract(options).lines);
});

// ---- Screens with no route (root-screens) ------------------------------------------------------

function noRoute(files: Record<string, string>, on = true) {
  const root = project(Object.fromEntries(Object.entries(files).map(([name, text]) => [`src/${name}`, text])));
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "project:tsconfig.json", settings: {}, rootScreens: on }).lines;
  return {
    screens: lines.flatMap((l) => (l.t === "node" && l.kind === "page" && l.name.startsWith("(no route) ") ? [l.name.slice("(no route) ".length)] : [])).sort(),
    targets: lines.flatMap((l) => (l.t === "edge" && l.kind === "routes-to" && l.source.includes("(no route)") ? [l.target] : [])).sort(),
    unknown: lines.flatMap((l) => (l.t === "blind" && l.detector === "root-screens" ? [l.reason] : [])),
    lines,
  };
}
const REACT_DOM = `declare module "react-dom" { export function render(e: any, at: any): void; }
declare module "react-dom/client" { export function createRoot(at: any): { render(e: any): void }; }
declare module "react-router-dom" { export const Route: any; export const Routes: any; export const BrowserRouter: any; }
declare module "acme-ui" { export const Theme: any; export const Toast: any; }`;
const INDEX = `import { createRoot } from "react-dom/client"; import App from "./App"; createRoot(document.body).render(<App />);`;
const WIDGET = (name: string) => `export default function ${name}() { return <div>${name}</div>; }`;

test("a component on another branch from the routes is a screen; the path to the routes isn't", () => {
  const r = noRoute({
    "types.d.ts": REACT_DOM, "index.tsx": INDEX, "Door.tsx": WIDGET("Door"), "Tasks.tsx": WIDGET("Tasks"),
    "Shell.tsx": `import { Routes, Route } from "react-router-dom"; import Tasks from "./Tasks"; export default function Shell() { return <Routes><Route path="/tasks" element={<Tasks />} /></Routes>; }`,
    "App.tsx": `import Door from "./Door"; import Shell from "./Shell"; export default function App({ who }: any) { return <div>{who ? <Shell /> : <Door />}</div>; }`,
  });
  assert.deepEqual(r.screens, ["Door"]);
  assert.deepEqual(r.targets, ["ts|src/Door.tsx#Door"]);
  assert.deepEqual(r.unknown, []);
});

test("an early return is a branch too, and library wrappers are walked through", () => {
  const r = noRoute({
    "types.d.ts": REACT_DOM, "index.tsx": INDEX, "Wait.tsx": WIDGET("Wait"),
    "App.tsx": `import { BrowserRouter, Routes } from "react-router-dom"; import { Theme } from "acme-ui"; import Wait from "./Wait";
      export default function App({ busy }: any) { if (busy) return <Wait />; return <Theme><BrowserRouter><Routes /></BrowserRouter></Theme>; }`,
  });
  assert.deepEqual(r.screens, ["Wait"]);
});

test("layout always shown beside the routes or round them isn't a screen", () => {
  const r = noRoute({
    "types.d.ts": REACT_DOM, "index.tsx": INDEX, "Bar.tsx": WIDGET("Bar"), "Frame.tsx": `export default function Frame({ children }: any) { return <main>{children}</main>; }`,
    "App.tsx": `import { Routes } from "react-router-dom"; import Bar from "./Bar"; import Frame from "./Frame";
      export default function App() { return <div><Bar /><Frame><Routes /></Frame></div>; }`,
  });
  assert.deepEqual(r.screens, []);
  assert.deepEqual(r.unknown, []);
});

test("a condition beside the routes and a component picked at runtime are unknown, never a guess", () => {
  const r = noRoute({
    "types.d.ts": REACT_DOM, "index.tsx": INDEX, "Note.tsx": WIDGET("Note"), "A.tsx": WIDGET("A"), "B.tsx": WIDGET("B"),
    "App.tsx": `import { Routes } from "react-router-dom"; import Note from "./Note"; import A from "./A"; import B from "./B";
      const views: any = { A, B };
      export default function App({ open, which, Pick }: any) { const Chosen = which ? A : B; return <div>{open && <Note />}<Routes />{views[which]}<Chosen /><Pick /></div>; }`,
  });
  assert.deepEqual(r.screens, []);
  assert.equal(r.unknown.length, 4);
  assert.ok(r.unknown.some((u) => u.includes("Note") && u.includes("beside the routes")));
  assert.ok(r.unknown.some((u) => u.includes("views[which]")));
  assert.ok(r.unknown.some((u) => u.includes("<Chosen>")));
  assert.ok(r.unknown.some((u) => u.includes("<Pick>")));
});

test("with no routes, what the root shows counts; a root showing only itself is the screen", () => {
  assert.deepEqual(noRoute({
    "types.d.ts": REACT_DOM, "index.tsx": INDEX, "One.tsx": WIDGET("One"), "Two.tsx": WIDGET("Two"),
    "App.tsx": `import One from "./One"; import Two from "./Two"; export default function App({ a }: any) { return a ? <One /> : <Two />; }`,
  }).screens, ["One", "Two"]);
  assert.deepEqual(noRoute({ "types.d.ts": REACT_DOM, "index.tsx": INDEX, "App.tsx": WIDGET("App") }).screens, ["App"]);
});

test("already routed components get no second page; test files and other render calls aren't roots", () => {
  const r = noRoute({
    "types.d.ts": REACT_DOM, "index.tsx": INDEX, "Door.tsx": WIDGET("Door"),
    "App.tsx": `import { Routes, Route } from "react-router-dom"; import Door from "./Door";
      export default function App({ who }: any) { return who ? <Routes><Route path="/door" element={<Door />} /></Routes> : <Door />; }`,
    "App.test.tsx": `import ReactDOM from "react-dom"; import Door from "./Door"; it("mounts", () => { ReactDOM.render(<Door />, document.body); });`,
    "other.tsx": `import Door from "./Door"; declare const engine: any; engine.render(<Door />);`,
  });
  assert.deepEqual(r.screens, []);
});

test("off, the output doesn't change; lazy, class and React.render roots are read; same names stay apart", () => {
  const files = {
    "types.d.ts": REACT_DOM, "index.tsx": `import ReactDOM from "react-dom"; import App from "./App"; ReactDOM.render(<App />, document.body);`,
    "a/Form.tsx": WIDGET("Form"), "b/Form.tsx": WIDGET("Form"), "Late.tsx": WIDGET("Late"),
    "App.tsx": `import { lazy } from "react"; import { Routes } from "react-router-dom"; import F from "./a/Form"; import G from "./b/Form";
      const Late = lazy(() => import("./Late"));
      export default class App { props: any; render() { const p = this.props; if (p.a) return <F />; if (p.b) return <G />; if (p.c) return <Late />; return <Routes />; } }`,
  };
  assert.deepEqual(noRoute(files).screens, ["Late", "src/a/Form", "src/b/Form"]);
  const off = noRoute(files, false);
  assert.deepEqual([off.screens, off.unknown], [[], []]);
});

// ---- Routes written with render or children (route-render) --------------------------------------

function rendered(files: Record<string, string>, on = true) {
  const root = project(Object.fromEntries(Object.entries(files).map(([name, text]) => [`src/${name}`, text])));
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "project:tsconfig.json", settings: {}, routeRender: on }).lines;
  return {
    pages: lines.flatMap((l) => (l.t === "node" && l.kind === "page" ? [l.name] : [])).sort(),
    targets: lines.flatMap((l) => (l.t === "edge" && l.kind === "routes-to" && l.detector === "route-render" ? [`${l.source.split("|")[1]} -> ${l.target.split("#")[1]}`] : [])).sort(),
    unknown: lines.flatMap((l) => (l.t === "blind" && l.detector === "route-render" ? [l.reason] : [])),
    lines,
  };
}
const ROUTES = (body: string) => `import { Route } from "react-router-dom"; import Tasks from "./Tasks"; import Help from "./Help";
const show = (props: any) => <Help {...props} />;
export function App({ picked }: any) { return <div>${body}</div>; }`;
const ROUTE_FILES = (body: string) => ({ "lib.d.ts": REACT_DOM, "Tasks.tsx": WIDGET("Tasks"), "Help.tsx": WIDGET("Help"), "App.tsx": ROUTES(body) });

test("render and children routes get a page naming the component in their JSX", () => {
  const r = rendered(ROUTE_FILES(`<Route path="/a" render={(props) => <Tasks {...props} />} />
    <Route path="/b" children={<Help />} />
    <Route path="/c"><Tasks /></Route>
    <Route path="/d" render={show} />
    <Route path="/e" render={(p) => (p.ok ? <Tasks /> : <Help />)} />`));
  assert.deepEqual(r.pages, ["/a", "/b", "/c", "/d", "/e"]);
  assert.deepEqual(r.targets, ["/a -> Tasks", "/b -> Help", "/c -> Tasks", "/d -> Help", "/e -> Help", "/e -> Tasks"]);
  assert.deepEqual(r.unknown, []);
});

test("a render route with no component to read is unknown, never a guess", () => {
  const r = rendered(ROUTE_FILES(`<Route path="/p" render={picked} /><Route path="/t" render={() => <div>text</div>} />`));
  assert.deepEqual(r.pages, ["/p", "/t"]);
  assert.equal(r.unknown.length, 2);
});

test("off, the output doesn't change; a route naming its component keeps it; nested routes stay routes", () => {
  const files = ROUTE_FILES(`<Route path="/a" render={() => <Tasks />} /><Route path="/n"><Route path="m" element={<Help />} /></Route>`);
  assert.deepEqual(rendered(files, false).pages, ["/n/m"]);
  const on = rendered(files);
  assert.deepEqual(on.pages, ["/a", "/n/m"]);
  assert.deepEqual(on.targets, ["/a -> Tasks"]);
  assert.deepEqual(rendered(ROUTE_FILES(`<Route path="/z" component={Tasks} render={() => <Help />} />`)).targets, []);
});

test("route guards are facts on each page they hold for, a parent's included (G8)", () => {
  const root = project({
    "src/stub.d.ts": "declare module '@angular/core' { export function Component(m: object): ClassDecorator; }",
    "src/list.component.ts": "import { Component } from '@angular/core';\n@Component({ selector: 'app-list', template: '<p></p>' })\nexport class ListComponent {}",
    "src/guards.ts": "export const authGuard = () => true;\nexport const adminGuard = () => true;",
    "src/routes.ts": [
      "import { ListComponent } from './list.component';",
      "import { authGuard, adminGuard } from './guards';",
      "export const routes = [",
      "  { path: 'open', component: ListComponent },",
      "  { path: '', canActivate: [authGuard], children: [",
      "    { path: 'list', component: ListComponent },",
      "    { path: 'admin', component: ListComponent, canActivate: [adminGuard] },",
      "  ] },",
      "];",
    ].join("\n"),
  }, { experimentalDecorators: true });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines;
  const guards = (page: string) => lines.flatMap((l) => l.t === "fact" && l.kind === "guard" && l.node.endsWith(`|${page}`) ? [l.data.guard.split("#")[1]] : []).sort();
  assert.deepEqual(guards("/open"), []);
  assert.deepEqual(guards("/list"), ["authGuard"]);
  assert.deepEqual(guards("/admin"), ["adminGuard", "authGuard"]);
});

test("a React button says which handler it runs and what it shows; the handler is a point that calls the hook (G5, G6)", () => {
  const root = project({
    "src/hooks.ts": "export function useAddSeries() { return { addSeries: (x: number) => x }; }\nexport function translate(key: string, o?: object) { return key; }\nexport function useCallback<T>(f: T, deps: unknown[]): T { return f; }",
    "src/Modal.tsx": [
      "import { useAddSeries, translate, useCallback } from './hooks';",
      "declare const Button: (p: { onPress?: () => void; children?: unknown }) => any;",
      "declare const Modal: (p: { onClose?: () => void; children?: unknown }) => any;",
      "export function AddModal(p: { title: string }) {",
      "  const { addSeries } = useAddSeries();",
      "  const handleAddPress = useCallback(() => { addSeries(1); }, [addSeries]);",
      "  return <Modal onClose={() => addSeries(0)}><Button onPress={handleAddPress}>{translate('AddSeriesWithTitle', { title: p.title })}</Button><Button onPress={() => addSeries(2)}>Cancel</Button></Modal>;",
      "}",
    ].join("\n"),
  }, { jsx: "preserve" });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines;
  const handler = "ts|src/Modal.tsx#AddModal.handleAddPress";
  assert.ok(lines.some((l) => l.t === "node" && l.id === handler && l.kind === "method"), "the handler is a point");
  assert.ok(lines.some((l) => l.t === "edge" && l.kind === "calls" && l.source === handler && l.target.endsWith("hooks.ts#useAddSeries")), "it calls the hook it got addSeries from");
  assert.ok(lines.some((l) => l.t === "edge" && l.kind === "uses" && l.source.endsWith("#AddModal") && l.target === handler), "the component uses it");
  const events = lines.flatMap((l) => l.t === "fact" && l.kind === "ui-event" ? [`${l.data.element} ${l.data.event} ${l.data.handler.split("#").pop()} [${l.data.keys}] [${l.data.text}]`] : []).sort();
  assert.deepEqual(events, ["Button onPress AddModal.handleAddPress [AddSeriesWithTitle] []", "Button onPress inline [] [Cancel]", "Modal onClose inline [] []"]);
});

test("an Angular (click) says which method it calls and what the element shows (G5, G6)", () => {
  const root = project({
    "src/stub.d.ts": "declare module '@angular/core' { export function Component(m: object): ClassDecorator; }",
    "src/star.component.ts": [
      "import { Component } from '@angular/core';",
      "@Component({ selector: 'app-star', template: '<button (click)=\"toggle()\" ngbTooltip=\"{{on ? t(\\'remove-from-want-to-read\\') : t(\\'add-to-want-to-read\\')}}\"><span>Star</span></button>' })",
      "export class StarComponent { on = false; toggle() {} t(k: string) { return k; } }",
    ].join("\n"),
  }, { experimentalDecorators: true });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines;
  const events = lines.flatMap((l) => l.t === "fact" && l.kind === "ui-event" ? [`${l.data.element} ${l.data.event} ${l.data.handler.split("#").pop()} [${l.data.keys}] [${l.data.text}]`] : []);
  assert.deepEqual(events, ["button click StarComponent.toggle [remove-from-want-to-read|add-to-want-to-read] [Star]"]);
});

test("a lazy route array is walked once, under its parent's path and guards, whatever it's called (F7)", () => {
  const root = project({
    "src/stub.d.ts": "declare module '@angular/core' { export function Component(m: object): ClassDecorator; }",
    "src/list.component.ts": "import { Component } from '@angular/core';\n@Component({ selector: 'app-list', template: '<p></p>' })\nexport class ListComponent {}",
    "src/guards.ts": "export const authGuard = () => true;",
    "src/browse.routes.ts": "import { ListComponent } from './list.component';\nexport const routes = [{ path: 'genres', component: ListComponent }];",
    "src/app.routes.ts": "import { authGuard } from './guards';\nexport const routes = [{ path: 'browse', canActivate: [authGuard], loadChildren: () => import('./browse.routes').then(m => m.routes) }];",
  }, { experimentalDecorators: true, module: "esnext" });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines;
  const pages = lines.flatMap((l) => l.t === "node" && l.kind === "page" ? [l.name] : []).sort();
  assert.deepEqual(pages, ["/browse/genres"]);
  assert.ok(lines.some((l) => l.t === "fact" && l.kind === "guard" && l.node.endsWith("|/browse/genres")));
});

test("constants' values and SignalR listeners, a list's items and enum members included (G9, G10)", () => {
  const root = project({
    "src/events.ts": "export enum EVENTS { ScanSeries = 'ScanSeries', SeriesAdded = 'SeriesAdded' }\nexport const PRIMARY = 'primary';\nexport const LIMIT = -5;\nexport const ON = true;",
    "src/hub.ts": [
      "import { EVENTS } from './events';",
      "declare const connection: { on(name: string, f: (x: unknown) => void): void };",
      "const listed = [EVENTS.ScanSeries, EVENTS.SeriesAdded];",
      "export function listen(name: string) {",
      "  connection.on('receiveMessage', () => {});",
      "  listed.forEach(event => connection.on(event, () => {}));",
      "  connection.on(name, () => {});",
      "}",
    ].join("\n"),
  });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines;
  const values = lines.flatMap((l) => l.t === "fact" && l.kind === "value" ? [`${l.node.split("#")[1]}=${l.data.value}`] : []).sort();
  assert.deepEqual(values, ["LIMIT=-5", "ON=true", "PRIMARY=primary"]);
  const on = lines.flatMap((l) => l.t === "fact" && l.kind === "signalr-on" ? [l.data.name] : []).sort();
  assert.deepEqual(on, ["", "ScanSeries", "SeriesAdded", "receiveMessage"]);
});

test("plain scripts and a view's inline scripts: jQuery calls and event bindings (N5)", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "stangent-js-"));
  fs.mkdirSync(path.join(root, "js"));
  fs.writeFileSync(path.join(root, "js/site.js"), [
    "function check() {",
    "  $.ajax({ url: '/api/orders', type: 'POST' });",
    "}",
    "$(function () { $('#check-email').click(check); $(document).on('click.cart', '.add', function () { $.get('/api/cart'); }); });",
    "document.getElementById('save').addEventListener('click', check);",
  ].join("\n"));
  const lines = extract({
    tsconfig: root, root, projectId: "p", settings: {}, files: [path.join(root, "js/site.js")],
    inline: [{ owner: "view:V.cshtml", file: "V.cshtml", line: 7, text: "\n  $.post('/api/x');\n  check();\n  $.post('__server_value__'); // don't\n  $.get('/api/__server_value__/items');" }],
  }).lines;
  const calls = lines.flatMap((l) => l.t === "fact" && l.kind === "call" ? [`${l.node} ${l.data.verb} ${l.data.url} ${l.file}:${l.line}`] : []).sort();
  assert.deepEqual(calls, [
    "ts|js/site.js#check POST /api/orders js/site.js:2",
    "file:js/site.js GET /api/cart js/site.js:4",
    "view:V.cshtml POST /api/x V.cshtml:8",
    "view:V.cshtml GET /api/{*}/items V.cshtml:11",
  ].sort());
  // A URL the server writes whole is made at runtime: ❓.
  assert.ok(lines.some((l) => l.t === "blind" && l.node === "view:V.cshtml" && l.line === 10 && l.reason === "HTTP POST to a URL built at runtime"));
  const events = lines.flatMap((l) => l.t === "fact" && l.kind === "dom-event" ? [`${l.data.selector} ${l.data.event} ${l.data.handler}`] : []).sort();
  assert.deepEqual(events, [".add click inline", "#check-email click ts|js/site.js#check", "#save click ts|js/site.js#check"].sort());
  // The inline script's code belongs to the view: what it calls is the view's line, and it declares no points of its own.
  assert.ok(lines.some((l) => l.t === "edge" && l.source === "view:V.cshtml" && l.target === "ts|js/site.js#check"));
  assert.ok(!lines.some((l) => l.t === "node" && l.file === "V.cshtml"));
});

test("a global in several plain scripts is a guess to each, and a handler is a function only when it is one (T2, T7)", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "stangent-js-"));
  fs.mkdirSync(path.join(root, "js"));
  fs.writeFileSync(path.join(root, "js/admin.js"), "function setLocation(url) { window.location = url; }");
  fs.writeFileSync(path.join(root, "js/public.js"), "function setLocation(url) { window.location.href = url; }");
  fs.writeFileSync(path.join(root, "js/page.js"), [
    "function go() { setLocation('/x'); }",
    "$('#many').click(function () { $.get('/api/a'); go(); });",
    "$('#one').click(function () { go(); });",
  ].join("\n"));
  const files = ["admin.js", "public.js", "page.js"].map((f) => path.join(root, "js", f));
  const lines = extract({ tsconfig: root, root, projectId: "p", settings: {}, files }).lines;
  const calls = lines.flatMap((l) => l.t === "edge" && l.source === "ts|js/page.js#go" && l.kind === "calls" ? [`${l.target} ${l.label}`] : []).sort();
  assert.deepEqual(calls, ["ts|js/admin.js#setLocation 1", "ts|js/public.js#setLocation 1"]);
  const events = lines.flatMap((l) => l.t === "fact" && l.kind === "dom-event" ? [`${l.data.selector} ${l.data.handler}`] : []).sort();
  assert.deepEqual(events, ["#many inline", "#one ts|js/page.js#go"]);
});

test("a handler that always stops the browser's own action says so (V2)", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "stangent-js-"));
  fs.mkdirSync(path.join(root, "js"));
  fs.writeFileSync(path.join(root, "js/page.js"), [
    "$('#refresh').click(function () { reload(); return false; });",
    "$('#maybe').click(function () { if (busy) return false; reload(); });",
    "document.getElementById('plain').addEventListener('click', function () { return false; });",
    "document.getElementById('stop').addEventListener('submit', function (e) { e.preventDefault(); reload(); });",
    "function reload() {}",
  ].join("\n"));
  const lines = extract({ tsconfig: root, root, projectId: "p", settings: {}, files: [path.join(root, "js/page.js")] }).lines;
  const events = lines.flatMap((l) => l.t === "fact" && l.kind === "dom-event" ? [`${l.data.selector} ${l.data.cancels}`] : []).sort();
  // jQuery honours `return false`; addEventListener doesn't; a conditional one isn't "always".
  assert.deepEqual(events, ["#maybe ", "#plain ", "#refresh yes", "#stop yes"]);
});

test("a URL built from a parameter takes each value its callers pass, paired with the verb under the same condition (D190)", () => {
  const root = project({
    "src/path.ts": "export const apiRoot = '/api/v5';\nconst getQueryPath = (path: string) => {\n  return apiRoot + path;\n};\nexport default getQueryPath;",
    "src/query.ts": "const getQueryString = (q?: Record<string, string>) => {\n  if (!q) {\n    return '';\n  }\n  return `?${new URLSearchParams(q)}`;\n};\nexport default getQueryString;",
    "src/save.ts": [
      "import getQueryPath from './path';",
      "import getQueryString from './query';",
      "declare function fetchJson(o: { path: string; method?: string }): void;",
      "export const useSave = (id: number | undefined, path: string) => {",
      "  const save = () => fetchJson({ path: getQueryPath(id ? `${path}/${id}` : path) + getQueryString({}), method: id ? 'PUT' : 'POST' });",
      "  return save;",
      "};",
      "export const useManage = (id: number | undefined, path: string) => useSave(id, path);",
    ].join("\n"),
    "src/indexers.ts": "import { useManage } from './save';\nconst PATH = '/indexer';\nexport const useManageIndexer = (id?: number) => useManage(id, PATH);",
    "src/lists.ts": "import { useManage } from './save';\nexport const useManageList = (id?: number) => useManage(id, '/importlist');",
  });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p",
    settings: { "frontend-http": { wrappers: ["fetchJson:path:method@GET"] } } }).lines;
  const calls = lines.filter((l) => l.t === "fact" && l.kind === "call").map((l) => l.t === "fact" ? `${l.data.verb} ${l.data.url} ${l.data.from} ${String(l.data.origin).split("#")[1]} ${l.data.of}` : "").sort();
  assert.deepEqual(calls, [
    `POST ${BASE}api/v5/importlist parameter useManageList 4`,
    `POST ${BASE}api/v5/indexer parameter useManageIndexer 4`,
    `PUT ${BASE}api/v5/importlist/{*} parameter useManageList 4`,
    `PUT ${BASE}api/v5/indexer/{*} parameter useManageIndexer 4`,
  ]);
  assert.equal(lines.filter((l) => l.t === "blind").length, 0);
});

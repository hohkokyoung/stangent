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

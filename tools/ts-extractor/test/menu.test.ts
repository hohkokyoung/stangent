import { test } from "node:test";
import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { extract } from "../src/extract.ts";

/** A throwaway project with these files and a tsconfig including them. */
function project(files: Record<string, string>, compilerOptions: Record<string, unknown> = {}): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "stangent-menu-"));
  for (const [name, text] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(root, name)), { recursive: true });
    fs.writeFileSync(path.join(root, name), text);
  }
  fs.writeFileSync(path.join(root, "package.json"), '{ "name": "t" }');
  fs.writeFileSync(path.join(root, "tsconfig.json"), JSON.stringify({ compilerOptions: { target: "ES2022", module: "ES2022", moduleResolution: "bundler", jsx: "preserve", ...compilerOptions }, include: ["src"] }));
  return root;
}

/** Each menu item as `path name|key → address (via)`, in output order. */
function menu(root: string): string[] {
  return extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines
    .flatMap((l) => (l.t === "fact" && l.kind === "menu-item" ? [`${l.data.path} ${l.data.name}|${l.data.key} → ${l.data.address} (${l.data.via})`] : []));
}

test("a React menu written as data: headings, items and children; a group with no name passes its items up (D221)", () => {
  const root = project({
    "src/translate.ts": "export default function translate(k: string) { return k; }",
    "src/Sidebar.tsx": [
      "import translate from './translate';",
      "const SETTINGS = '/settings';",
      "const GROUPS = [",
      "  { label: () => translate('Media'), items: [",
      "    { title: () => translate('Series'), to: '/series', children: [ { title: () => translate('AddNew'), to: '/add/new' } ] },",
      "    { title: 'Calendar', to: '/calendar' },",
      "  ] },",
      "  { label: () => translate('Activity'), items: [ { title: () => translate('Queue'), to: '/activity/queue' } ] },",
      "  { items: [ { title: () => translate('Settings'), to: SETTINGS }, { title: 'System', to: '/system/' + Math.random() } ] },",
      "];",
      "export function Sidebar() { return GROUPS.length; }",
    ].join("\n"),
  });
  assert.deepEqual(menu(root), [
    "1 |Media →  (data)",
    "1.1 |Series → /series (data)",
    "1.1.1 |AddNew → /add/new (data)",
    "1.2 Calendar| → /calendar (data)",
    "2 |Activity →  (data)",
    "2.1 |Queue → /activity/queue (data)",
    "3 |Settings → /settings (data)",
  ]);
});

test("routes, short lists and runtime addresses aren't menus", () => {
  const root = project({
    "src/routes.ts": [
      "declare const A: unknown;",
      "export const routes = [ { path: 'a', title: 'A', component: A }, { path: 'b', title: 'B', component: A }, { path: 'c', title: 'C', component: A } ];",
      "export const two = [ { title: 'One', to: '/one' }, { title: 'Two', to: '/two' } ];",
      "declare const id: string;",
      "export const built = [ { title: 'X', to: '/x/' + id }, { title: 'Y', to: `/y/${id}` }, { title: 'Z', to: id }, { title: 'Top', to: '#top' } ];",
    ].join("\n"),
  });
  assert.deepEqual(menu(root), []);
});

test("a React menu in markup: links side by side are a list, a list right after a link is its children", () => {
  const root = project({
    "src/Link.tsx": "export function Link(p: { to: string; children?: unknown }) { return null; }",
    "src/Nav.tsx": [
      "import { Link } from './Link';",
      "declare function t(k: string): string;",
      "declare const open: boolean;",
      "export function Nav() {",
      "  return (<nav><ul>",
      "    <li><Link to=\"/home\">Home</Link></li>",
      "    <li><Link to=\"/library\">{t('library')}</Link><ul><li><Link to=\"/library/books\">Books</Link></li><li><Link to=\"/library/comics\">Comics</Link></li></ul></li>",
      "    {open && <li><a href=\"/about\" title=\"About us\" /></li>}",
      "    <li><a href=\"#\">Nowhere</a></li>",
      "  </ul></nav>);",
      "}",
    ].join("\n"),
  });
  assert.deepEqual(menu(root), [
    "1 Home| → /home (markup)",
    "2 |library → /library (markup)",
    "2.1 Books| → /library/books (markup)",
    "2.2 Comics| → /library/comics (markup)",
    "3 About us| → /about (markup)",
  ]);
});

test("an Angular menu in markup: link= and routerLink through @if and @for, keys under transloco's prefix", () => {
  const root = project({
    "src/stub.d.ts": "declare module '@angular/core' { export function Component(m: object): ClassDecorator; }",
    "src/side-nav.component.ts": [
      "import { Component } from '@angular/core';",
      "@Component({ selector: 'app-side-nav', template: `",
      "<ng-container *transloco=\"let t; prefix: 'side-nav'\">",
      "  <div class=\"nav\">",
      "    <app-item [title]=\"t('home')\" link=\"/home/\"></app-item>",
      "    @if (admin) { <app-item [title]=\"t('settings')\" link=\"/settings\" /> }",
      "    @for (l of libs; track l.id) { <app-item [title]=\"l.name\" [link]=\"'/library/' + l.id\" /> }",
      "    <a [routerLink]=\"['/', 'want-to-read']\">{{ t('want-to-read') }}</a>",
      "    <a routerLink=\"/lists\">Reading lists</a>",
      "  </div>",
      "</ng-container>` })",
      "export class SideNavComponent { admin = true; libs: { id: number; name: string }[] = []; }",
    ].join("\n"),
  }, { experimentalDecorators: true });
  assert.deepEqual(menu(root), [
    "1 |side-nav.home → /home/ (markup)",
    "2 |side-nav.settings → /settings (markup)",
    "3 |side-nav.want-to-read → /want-to-read (markup)",
    "4 Reading lists| → /lists (markup)",
  ]);
});

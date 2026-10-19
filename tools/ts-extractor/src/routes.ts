import ts from "typescript";
import type { Context } from "./extract.ts";
import { stringValue } from "./values.ts";

/**
 * Pages: the frontend's entry points (D31). Angular Router and object-style React Router routes
 * (`{ path, component | loadComponent | loadChildren | element | Component, children }`), and JSX
 * `<Route path element>` trees. Each page routes to the component it shows (✅ when the checker
 * resolves it). Child paths are joined to their parents'.
 */
export function pages(ctx: Context, sf: ts.SourceFile) {
  const nested = new Set<ts.Node>();
  // Arrays reached through `children` or `loadChildren` are walked from their parent, not as roots.
  const markChildren = (arr: ts.ArrayLiteralExpression) => {
    for (const el of arr.elements) {
      if (!ts.isObjectLiteralExpression(el)) continue;
      const children = prop(el, "children");
      if (children && ts.isArrayLiteralExpression(children)) { nested.add(children); markChildren(children); }
    }
  };
  const visit = (n: ts.Node) => {
    if (ts.isArrayLiteralExpression(n) && isRouteArray(n)) markChildren(n);
    ts.forEachChild(n, visit);
  };
  visit(sf);

  const roots = (n: ts.Node) => {
    if (ts.isArrayLiteralExpression(n) && isRouteArray(n) && !nested.has(n) && !referencedByLoadChildren(ctx, n)) {
      routeArray(ctx, n, "", new Set());
      return;
    }
    if ((ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n)) && tagName(n) === "Route" && !insideRoute(n)) {
      jsxRoute(ctx, n, "");
      return;
    }
    ts.forEachChild(n, roots);
  };
  roots(sf);
}

const ROUTE_TARGETS = ["component", "loadComponent", "loadChildren", "children", "redirectTo", "element", "Component", "lazy"];

function isRouteArray(arr: ts.ArrayLiteralExpression): boolean {
  const objects = arr.elements.filter(ts.isObjectLiteralExpression);
  return objects.length > 0 && objects.every((o) => prop(o, "path") !== null || ROUTE_TARGETS.some((k) => prop(o, k) !== null))
    && objects.some((o) => prop(o, "path") !== null && ROUTE_TARGETS.some((k) => prop(o, k) !== null));
}

function referencedByLoadChildren(ctx: Context, arr: ts.ArrayLiteralExpression): boolean {
  // export const SERIES_ROUTES = [...] — also reachable from some loadChildren; walked from there when it is.
  const decl = arr.parent;
  return ts.isVariableDeclaration(decl) && ts.isIdentifier(decl.name) && /routes?$/i.test(decl.name.text)
    && !/^(app)?routes?$/i.test(decl.name.text) && loadChildrenTargets(ctx).has(decl);
}

let loadChildrenCache: Set<ts.Node> | null = null;
function loadChildrenTargets(ctx: Context): Set<ts.Node> {
  if (loadChildrenCache) return loadChildrenCache;
  const targets = new Set<ts.Node>();
  for (const sf of ctx.program.getSourceFiles().filter(ctx.isOwn)) {
    const visit = (n: ts.Node) => {
      if (ts.isPropertyAssignment(n) && ts.isIdentifier(n.name) && n.name.text === "loadChildren") {
        const target = lazyTarget(ctx, n.initializer);
        if (target) for (const d of target.declarations ?? []) targets.add(d);
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }
  return (loadChildrenCache = targets);
}

function routeArray(ctx: Context, arr: ts.ArrayLiteralExpression, prefix: string, visiting: Set<ts.Node>) {
  if (visiting.has(arr)) return;
  visiting.add(arr);
  for (const el of arr.elements) {
    if (!ts.isObjectLiteralExpression(el)) continue;
    const isIndex = prop(el, "index")?.kind === ts.SyntaxKind.TrueKeyword;
    const own = isIndex ? "" : stringValue(prop(el, "path") ?? undefined, ctx.checker);
    const full = join(prefix, own ?? "");
    const target = componentOf(ctx, el);
    if (own !== null && (target.ids.length > 0 || target.unresolved)) addPage(ctx, el, full, target);
    const children = prop(el, "children");
    if (children && ts.isArrayLiteralExpression(children)) routeArray(ctx, children, full, visiting);
    const load = prop(el, "loadChildren");
    if (load) {
      for (const d of lazyTarget(ctx, load)?.declarations ?? [])
        if (ts.isVariableDeclaration(d) && d.initializer && ts.isArrayLiteralExpression(d.initializer)) routeArray(ctx, d.initializer, full, visiting);
    }
  }
}

interface Target { ids: string[]; unresolved: boolean }

function componentOf(ctx: Context, route: ts.ObjectLiteralExpression): Target {
  for (const key of ["component", "Component"]) {
    const e = prop(route, key);
    if (e) {
      const id = ctx.symbolId(ctx.checker.getSymbolAtLocation(e));
      return { ids: id ? [id] : [], unresolved: !id };
    }
  }
  const element = prop(route, "element");
  if (element) return jsxComponents(ctx, element);
  const lazy = prop(route, "loadComponent") ?? prop(route, "lazy");
  if (lazy) {
    const id = ctx.symbolId(lazyTarget(ctx, lazy));
    return { ids: id ? [id] : [], unresolved: !id };
  }
  return { ids: [], unresolved: false };
}

/** Every component an element renders: `<ProtectedRoute><Tasks /></ProtectedRoute>` shows both. */
function jsxComponents(ctx: Context, e: ts.Node): Target {
  const ids: string[] = [];
  let tags = 0;
  const visit = (n: ts.Node) => {
    const tag = ts.isJsxOpeningElement(n) || ts.isJsxSelfClosingElement(n) ? n.tagName : null;
    if (tag && /^[A-Z]/.test(tag.getText())) {
      tags++;
      const id = ctx.symbolId(ctx.checker.getSymbolAtLocation(tag));
      if (id && !ids.includes(id)) ids.push(id);
    }
    ts.forEachChild(n, visit);
  };
  visit(e);
  return { ids, unresolved: tags > 0 && ids.length === 0 };
}

/** `() => import('./x').then(m => m.Thing)` → the Thing symbol; `() => import('./x')` → the module's default export. */
function lazyTarget(ctx: Context, e: ts.Expression): ts.Symbol | undefined {
  let found: ts.Symbol | undefined;
  const visit = (n: ts.Node) => {
    if (found) return;
    if (ts.isPropertyAccessExpression(n) && n.expression && ts.isIdentifier(n.expression)) {
      const s = ctx.checker.getSymbolAtLocation(n.name);
      if (s && ctx.symbolId(s)) { found = s; return; }
      if (s && (s.declarations ?? []).some((d) => ts.isVariableDeclaration(d))) { found = s; return; }
    }
    if (ts.isCallExpression(n) && n.expression.kind === ts.SyntaxKind.ImportKeyword && n.arguments[0]) {
      const mod = ctx.checker.getSymbolAtLocation(n.arguments[0]);
      const def = mod ? ctx.checker.getExportsOfModule(mod).find((x) => x.escapedName === "default") : undefined;
      if (def && !found) found = def;
    }
    ts.forEachChild(n, visit);
  };
  visit(e);
  return found;
}

function jsxRoute(ctx: Context, el: ts.JsxElement | ts.JsxSelfClosingElement, prefix: string) {
  const attrs = ts.isJsxElement(el) ? el.openingElement.attributes : el.attributes;
  let own: string | null = null;
  let target: Target = { ids: [], unresolved: false };
  let index = false;
  for (const a of attrs.properties) {
    if (!ts.isJsxAttribute(a) || !ts.isIdentifier(a.name)) continue;
    const v = a.initializer;
    const expr = v && ts.isJsxExpression(v) ? v.expression : v && ts.isStringLiteral(v) ? v : undefined;
    if (a.name.text === "path") own = stringValue(expr, ctx.checker);
    if (a.name.text === "index") index = true;
    if (a.name.text === "element" && expr) target = jsxComponents(ctx, expr);
    if ((a.name.text === "component" || a.name.text === "Component") && expr) {
      const id = ctx.symbolId(ctx.checker.getSymbolAtLocation(expr));
      target = { ids: id ? [id] : [], unresolved: !id };
    }
  }
  const full = own === null ? prefix : join(prefix, own);
  if ((own !== null || index) && (target.ids.length > 0 || target.unresolved)) addPage(ctx, el, full, target);
  if (ts.isJsxElement(el))
    for (const child of el.children)
      if ((ts.isJsxElement(child) || ts.isJsxSelfClosingElement(child)) && tagName(child) === "Route") jsxRoute(ctx, child, full);
}

function addPage(ctx: Context, at: ts.Node, route: string, target: Target) {
  const id = `page:${ctx.o.projectId.replace(/^project:/, "")}|${route}`;
  const file = ctx.rel(at.getSourceFile().fileName);
  const line = ctx.line(at);
  ctx.out.node({ id, kind: "page", name: route, project: ctx.o.projectId, file, line, endLine: line, isTest: false });
  for (const t of target.ids) ctx.out.edge(id, t, "routes-to", 0, "routes");
  if (target.ids.length === 0) ctx.out.blind(id, file, line, `page ${route}: its component couldn't be resolved`, "routes");
}

function join(prefix: string, own: string): string {
  if (own.startsWith("/")) return normalize(own);
  return normalize(`${prefix}/${own}`);
}

const normalize = (p: string) => ("/" + p.split("/").filter(Boolean).join("/"));

function prop(o: ts.ObjectLiteralExpression, name: string): ts.Expression | null {
  for (const p of o.properties) {
    if (ts.isPropertyAssignment(p) && (ts.isIdentifier(p.name) || ts.isStringLiteral(p.name)) && p.name.text === name) return p.initializer;
    if (ts.isShorthandPropertyAssignment(p) && p.name.text === name) return p.name;
    if (ts.isMethodDeclaration(p) && ts.isIdentifier(p.name) && p.name.text === name) return p as unknown as ts.Expression;
  }
  return null;
}

function firstJsxTag(e: ts.Node): ts.JsxTagNameExpression | null {
  if (ts.isJsxElement(e)) return e.openingElement.tagName;
  if (ts.isJsxSelfClosingElement(e)) return e.tagName;
  let found: ts.JsxTagNameExpression | null = null;
  ts.forEachChild(e, (c) => { if (!found) found = firstJsxTag(c); });
  return found;
}

function tagName(el: ts.JsxElement | ts.JsxSelfClosingElement): string {
  const t = ts.isJsxElement(el) ? el.openingElement.tagName : el.tagName;
  return t.getText();
}

function insideRoute(el: ts.Node): boolean {
  for (let p = el.parent; p; p = p.parent)
    if ((ts.isJsxElement(p) && p.openingElement.tagName.getText() === "Route")) return true;
  return false;
}

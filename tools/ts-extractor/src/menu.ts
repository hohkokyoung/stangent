import ts from "typescript";
import { parseTemplate } from "@angular/compiler";
import type { Context } from "./extract.ts";
import { LABEL_ATTRS, TEXT_CALLS } from "./ui.ts";
import { RUNTIME, BASE, stringValue } from "./values.ts";

/**
 * D221: the app's menu, as `menu-item` facts. A menu item is a name plus a constant address, written in markup
 * (`link=`, `routerLink`, `to=`, `href`) or as data (an object with a name-like and an address-like property, its
 * children nested under it); an item with a name and children but no address is a heading (Sonarr's "Activity"). A
 * list of three or more items is a menu: the outermost such list's items are its top level, and each item's children
 * hang under it. Names are the item's own words, or the key it passes to `translate`/`t`/the `translate` pipe
 * (transloco's `prefix`/`read` in front), which layer 3 looks up in the translation files.
 */

/** Property names that name an item, in the order they're preferred. */
export const NAME_PROPS = ["title", "label", "text", "name", "caption", "displayName", "header", "heading", "systemName"];
/** Property and attribute names that hold where an item leads. */
export const ADDRESS_PROPS = ["to", "href", "link", "url", "path", "route", "routerLink"];
/** An object holding one of these is a route's definition, not a menu item. */
const ROUTE_KEYS = new Set(["component", "loadComponent", "loadChildren", "element", "Component", "lazy", "redirectTo", "pathMatch"]);

export interface MenuItem {
  name: string;
  key: string;
  address: string;
  via: string;
  owner: string;
  file: string;
  line: number;
  children: MenuItem[];
}

/** What a piece of code holds: one item (with its children), or a list sealed below it. */
type Unit = { item: MenuItem } | { list: MenuItem[]; line: number; owner: string; file: string };

const lower = (s: string) => s.toLowerCase();
const isName = (n: string) => NAME_PROPS.some((p) => lower(p) === lower(n));
const isAddress = (n: string) => ADDRESS_PROPS.some((p) => lower(p) === lower(n));
/** A constant address: known in full, and not a page anchor or a script (`#`, `javascript:`), which lead nowhere. */
const constant = (v: string | null) => (v !== null && v.trim() !== "" && !v.includes(RUNTIME) && !v.includes(BASE) && !/^\s*(#|javascript:)/i.test(v) ? v : null);

/**
 * Markup units: an item takes the list right after it as its children (`<li><a>Parent</a><ul>…</ul></li>`); two or more
 * items side by side are a list, sealed; one passes up unchanged, so wrappers holding one item (`<li>`) don't count.
 */
function fold(units: Unit[]): Unit[] {
  const out: Unit[] = [];
  for (const u of units) {
    const prev = out[out.length - 1];
    if ("list" in u && prev && "item" in prev && prev.item.children.length === 0) { prev.item.children = u.list; continue; }
    out.push(u);
  }
  const items = out.filter((u): u is { item: MenuItem } => "item" in u);
  if (items.length < 2) return out;
  const first = items[0].item;
  const sealed: Unit = { list: items.map((u) => u.item), line: first.line, owner: first.owner, file: first.file };
  return [sealed, ...out.filter((u) => "list" in u)];
}

/** Every list left at the top: a menu when it holds three or more items. */
function emit(ctx: Context, units: Unit[]) {
  for (const u of units) {
    if (!("list" in u) || u.list.length < 3) continue;
    const menu = `${u.file}:${u.line}`;
    const write = (items: MenuItem[], prefix: string) => items.forEach((it, i) => {
      const at = prefix === "" ? `${i + 1}` : `${prefix}.${i + 1}`;
      ctx.out.fact("menu", "menu-item", it.owner, it.file, it.line, { menu, path: at, name: it.name, key: it.key, address: it.address, via: it.via });
      write(it.children, at);
    });
    write(u.list, "");
  }
}

// ---- Data: arrays of objects ----

function propName(p: ts.ObjectLiteralElementLike): string | null {
  return p.name && (ts.isIdentifier(p.name) || ts.isStringLiteral(p.name)) ? p.name.text : null;
}

/** A name-like value: words as written, or the key a translate call is given (also inside `() => translate('X')`). */
function label(ctx: Context, e: ts.Expression): { name: string; key: string } | null {
  while (ts.isParenthesizedExpression(e) || ts.isAsExpression(e)) e = e.expression;
  if (ts.isArrowFunction(e) || ts.isFunctionExpression(e)) {
    const body = e.body;
    if (ts.isBlock(body)) {
      const ret = body.statements.length === 1 && ts.isReturnStatement(body.statements[0]) ? body.statements[0].expression : undefined;
      return ret ? label(ctx, ret) : null;
    }
    return label(ctx, body);
  }
  if (ts.isCallExpression(e)) {
    const callee = ts.isPropertyAccessExpression(e.expression) ? e.expression.name.text : ts.isIdentifier(e.expression) ? e.expression.text : "";
    const first = e.arguments[0];
    if (TEXT_CALLS.has(callee) && first && (ts.isStringLiteral(first) || ts.isNoSubstitutionTemplateLiteral(first))) return { name: "", key: first.text };
    return null;
  }
  const v = constant(stringValue(e, ctx.checker));
  return v ? { name: v, key: "" } : null;
}

/** The array literal an expression is: written there, or a constant naming one. */
function arrayOf(ctx: Context, e: ts.Expression): ts.ArrayLiteralExpression | null {
  while (ts.isParenthesizedExpression(e) || ts.isAsExpression(e) || ts.isSatisfiesExpression(e)) e = e.expression;
  if (ts.isArrayLiteralExpression(e)) return e;
  if (ts.isIdentifier(e)) {
    const decl = ctx.checker.getSymbolAtLocation(e)?.valueDeclaration;
    if (decl && ts.isVariableDeclaration(decl) && decl.initializer && ts.isArrayLiteralExpression(decl.initializer)
        && ts.getCombinedNodeFlags(decl) & ts.NodeFlags.Const) return decl.initializer;
  }
  return null;
}

function objectItem(ctx: Context, o: ts.ObjectLiteralExpression, seen: Set<ts.Node>): { item: MenuItem | null; inner: MenuItem[] } {
  let name = "", key = "", address = "", addressSeen = false;
  const inner: MenuItem[] = [];
  const props = o.properties.map((p) => ({ p, n: propName(p) }));
  if (props.some(({ n }) => n !== null && ROUTE_KEYS.has(n))) return { item: null, inner };
  for (const nameProp of NAME_PROPS)
    for (const { p, n } of props) {
      if (n === null || lower(n) !== lower(nameProp) || !ts.isPropertyAssignment(p)) continue;
      const l = label(ctx, p.initializer);
      if (l?.key && !key) key = l.key;
      if (l?.name && !name) name = l.name;
    }
  for (const { p, n } of props) {
    if (n === null || !ts.isPropertyAssignment(p)) continue;
    if (isAddress(n)) {
      addressSeen = true;
      const v = constant(stringValue(p.initializer, ctx.checker));
      if (v && !address) address = v;
      continue;
    }
    if (isName(n)) continue;
    const arr = arrayOf(ctx, p.initializer);
    if (arr && !seen.has(arr)) inner.push(...arrayItems(ctx, arr, seen));
  }
  const named = name !== "" || key !== "";
  if (named && (address !== "" || (!addressSeen && inner.length > 0)))
    return { item: { name, key, address, via: "data", owner: ctx.ownerId(o), file: ctx.rel(o.getSourceFile().fileName), line: ctx.line(o), children: inner }, inner: [] };
  return { item: null, inner };
}

/** An array's items: its item objects, and the items of any plain object in it (a group with no name passes its items up). */
function arrayItems(ctx: Context, arr: ts.ArrayLiteralExpression, seen: Set<ts.Node>): MenuItem[] {
  seen.add(arr);
  const items: MenuItem[] = [];
  for (const el of arr.elements) {
    let e = el;
    while (ts.isParenthesizedExpression(e) || ts.isAsExpression(e) || ts.isSatisfiesExpression(e)) e = e.expression;
    if (!ts.isObjectLiteralExpression(e)) continue;
    const { item, inner } = objectItem(ctx, e, seen);
    if (item) items.push(item);
    else items.push(...inner);
  }
  return items;
}

function dataMenus(ctx: Context, sf: ts.SourceFile) {
  const seen = new Set<ts.Node>();
  const visit = (n: ts.Node) => {
    if (ts.isArrayLiteralExpression(n) && !seen.has(n)) {
      const local = new Set<ts.Node>();
      const items = arrayItems(ctx, n, local);
      if (items.length >= 3) {
        for (const x of local) seen.add(x);
        emit(ctx, [{ list: items, line: items[0].line, owner: items[0].owner, file: items[0].file }]);
        return;
      }
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

// ---- Markup: JSX ----

function jsxAttrs(el: ts.JsxElement | ts.JsxSelfClosingElement) {
  return (ts.isJsxElement(el) ? el.openingElement.attributes : el.attributes).properties;
}

function jsxAddress(ctx: Context, el: ts.JsxElement | ts.JsxSelfClosingElement): string | null {
  for (const a of jsxAttrs(el)) {
    if (!ts.isJsxAttribute(a) || !ts.isIdentifier(a.name) || !isAddress(a.name.text) || !a.initializer) continue;
    if (ts.isStringLiteral(a.initializer)) return constant(a.initializer.text);
    if (ts.isJsxExpression(a.initializer) && a.initializer.expression) return constant(stringValue(a.initializer.expression, ctx.checker));
  }
  return null;
}

function jsxLabel(el: ts.JsxElement | ts.JsxSelfClosingElement): { name: string; key: string } {
  const keys: string[] = [], texts: string[] = [];
  const add = (list: string[], v: string) => { v = v.replace(/\s+/g, " ").trim(); if (v && !list.includes(v)) list.push(v); };
  const visit = (n: ts.Node) => {
    if (n !== el && (ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n)) && jsxAttrs(n).some((a) => ts.isJsxAttribute(a) && ts.isIdentifier(a.name) && isAddress(a.name.text))) return;
    if (ts.isJsxText(n)) add(texts, n.text);
    if (ts.isCallExpression(n)) {
      const callee = ts.isPropertyAccessExpression(n.expression) ? n.expression.name.text : ts.isIdentifier(n.expression) ? n.expression.text : "";
      const first = n.arguments[0];
      if (TEXT_CALLS.has(callee) && first && (ts.isStringLiteral(first) || ts.isNoSubstitutionTemplateLiteral(first))) add(keys, first.text);
    }
    if (ts.isJsxAttribute(n) && ts.isIdentifier(n.name) && LABEL_ATTRS.has(n.name.text) && n.initializer && ts.isStringLiteral(n.initializer)) add(texts, n.initializer.text);
    ts.forEachChild(n, visit);
  };
  visit(el);
  return { name: texts.join(" "), key: keys[0] ?? "" };
}

function jsxUnits(ctx: Context, n: ts.Node): Unit[] {
  if (ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n)) {
    const inside = ts.isJsxElement(n) ? fold(n.children.flatMap((c) => jsxUnits(ctx, c))) : [];
    const address = jsxAddress(ctx, n);
    if (address !== null) {
      const { name, key } = jsxLabel(n);
      if (name !== "" || key !== "") {
        const children = inside.flatMap((u) => ("list" in u ? u.list : [u.item]));
        return [{ item: { name, key, address, via: "markup", owner: ctx.ownerId(n), file: ctx.rel(n.getSourceFile().fileName), line: ctx.line(n), children } }];
      }
    }
    return inside;
  }
  if (ts.isJsxFragment(n)) return fold(n.children.flatMap((c) => jsxUnits(ctx, c)));
  const out: Unit[] = [];
  ts.forEachChild(n, (c) => { out.push(...jsxUnits(ctx, c)); });
  return out;
}

function jsxMenus(ctx: Context, sf: ts.SourceFile) {
  const visit = (n: ts.Node) => {
    if (ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n) || ts.isJsxFragment(n)) {
      emit(ctx, fold(jsxUnits(ctx, n)));
      return;
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

// ---- Markup: Angular templates ----

const SPANS = new Set(["sourceSpan", "startSourceSpan", "endSourceSpan", "keySpan", "valueSpan", "nameSpan", "handlerSpan", "i18n"]);
/** Parts of a template node that are expressions or bindings, never markup. */
const NOT_MARKUP = new Set(["attributes", "inputs", "outputs", "references", "variables", "templateAttrs", "expression", "value", "handler",
  "item", "contextVariables", "trackBy", "parameters", "triggers", "prefetchTriggers", "hydrateTriggers", "expressionAlias"]);
const ctorName = (o: unknown) => ((o as { constructor?: { name?: string } } | null)?.constructor?.name ?? "");
type Obj = Record<string, unknown>;

function isElement(o: Obj): boolean { return typeof o.name === "string" && Array.isArray(o.attributes) && Array.isArray(o.children) && Array.isArray(o.inputs); }

function astOf(v: unknown): Obj | null {
  const o = v as Obj | null;
  if (!o || typeof o !== "object") return null;
  return (o.ast as Obj | undefined) ?? o;
}

function ngAddress(o: Obj): string | null {
  for (const a of o.attributes as Obj[])
    if (typeof a.name === "string" && isAddress(a.name) && typeof a.value === "string") return constant(a.value);
  for (const a of o.inputs as Obj[]) {
    if (typeof a.name !== "string" || !isAddress(a.name)) continue;
    const ast = astOf(a.value);
    if (ast && ctorName(ast) === "LiteralPrimitive" && typeof ast.value === "string") return constant(ast.value);
    if (ast && ctorName(ast) === "LiteralArray" && Array.isArray(ast.expressions)) {
      const parts = (ast.expressions as Obj[]).map((x) => (ctorName(x) === "LiteralPrimitive" && typeof x.value === "string" ? x.value : null));
      if (parts.length > 0 && parts.every((p) => p !== null)) return constant(parts.join("/").replace(/\/{2,}/g, "/"));
    }
    return null;
  }
  return null;
}

function ngCollect(node: unknown, visit: (n: Obj) => boolean, seen = new Set<object>()) {
  if (!node || typeof node !== "object" || seen.has(node as object)) return;
  seen.add(node as object);
  if (Array.isArray(node)) { for (const x of node) ngCollect(x, visit, seen); return; }
  const o = node as Obj;
  if (!visit(o)) return;
  for (const [k, v] of Object.entries(o)) if (!SPANS.has(k)) ngCollect(v, visit, seen);
}

function ngLabel(o: Obj, prefix: string): { name: string; key: string } {
  const keys: string[] = [], texts: string[] = [];
  const add = (list: string[], v: string) => { v = v.replace(/\s+/g, " ").trim(); if (v && !list.includes(v)) list.push(v); };
  ngCollect([o.attributes, o.inputs, o.children], (n) => {
    if (n !== o && isElement(n) && ngAddress(n) !== null) return false; // a nested item speaks for itself
    const ctor = ctorName(n);
    if (ctor === "Text" && typeof n.value === "string") add(texts, n.value);
    if (ctor === "TextAttribute" && typeof n.name === "string" && LABEL_ATTRS.has(n.name) && typeof n.value === "string") add(texts, n.value);
    const recv = n.receiver as Obj | undefined;
    const args = n.args as Obj[] | undefined;
    if (/^Call|SafeCall/.test(ctor) && recv && typeof recv.name === "string" && TEXT_CALLS.has(recv.name)
        && args?.[0] && ctorName(args[0]) === "LiteralPrimitive" && typeof args[0].value === "string") add(keys, args[0].value);
    if (ctor === "BindingPipe" && (n.name === "translate" || n.name === "transloco") && ctorName(n.exp) === "LiteralPrimitive" && typeof (n.exp as Obj).value === "string")
      add(keys, (n.exp as { value: string }).value);
    return true;
  });
  const key = keys[0] ?? "";
  return { name: texts.join(" "), key: key && prefix ? `${prefix}.${key}` : key };
}

/** transloco's `*transloco="let t; prefix: 'side-nav'"` (or `read:`): the scope its keys are read under. */
function translocoPrefix(o: Obj): string | null {
  for (const a of (Array.isArray(o.templateAttrs) ? o.templateAttrs : []) as Obj[]) {
    if (typeof a.name !== "string" || !/^transloco(Prefix|Read)$/.test(a.name)) continue;
    const ast = astOf(a.value);
    if (ast && ctorName(ast) === "LiteralPrimitive" && typeof ast.value === "string") return ast.value;
    if (typeof a.value === "string") return a.value;
  }
  return null;
}

function ngUnits(ctx: Context, owner: string, file: string, line0: number, node: unknown, prefix: string): Unit[] {
  if (!node || typeof node !== "object") return [];
  if (Array.isArray(node)) return node.flatMap((x) => ngUnits(ctx, owner, file, line0, x, prefix));
  const o = node as Obj;
  const scope = translocoPrefix(o) ?? prefix;
  if (isElement(o)) {
    const inside = fold(ngUnits(ctx, owner, file, line0, o.children, scope));
    const address = ngAddress(o);
    if (address !== null) {
      const { name, key } = ngLabel(o, scope);
      if (name !== "" || key !== "") {
        const line = line0 + ((o.sourceSpan as { start?: { line?: number } } | undefined)?.start?.line ?? 0);
        const children = inside.flatMap((u) => ("list" in u ? u.list : [u.item]));
        return [{ item: { name, key, address, via: "markup", owner, file, line, children } }];
      }
    }
    return inside;
  }
  const out: Unit[] = [];
  for (const [k, v] of Object.entries(o)) if (!SPANS.has(k) && !NOT_MARKUP.has(k) && v && typeof v === "object") out.push(...ngUnits(ctx, owner, file, line0, v, scope));
  return isTemplateLike(o) ? fold(out) : out;
}

/** An `<ng-template>` or a structural directive's wrapper: it holds markup like an element does. */
function isTemplateLike(o: Obj): boolean { return typeof o.tagName === "string" && Array.isArray(o.children); }

function angularMenus(ctx: Context, sf: ts.SourceFile) {
  for (const c of ctx.angular.components.values()) {
    if (c.cls.getSourceFile() !== sf || !c.template) continue;
    const t = c.template;
    const templateId = `ts|${t.file}#${c.id.slice(c.id.indexOf("#") + 1)}.template`;
    let parsed;
    try { parsed = parseTemplate(t.text, t.file, { preserveWhitespaces: false }); } catch { continue; }
    emit(ctx, fold(ngUnits(ctx, templateId, t.file, t.line, parsed.nodes, "")));
  }
}

export function menuFacts(ctx: Context, sf: ts.SourceFile) {
  dataMenus(ctx, sf);
  jsxMenus(ctx, sf);
  angularMenus(ctx, sf);
}

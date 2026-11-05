import ts from "typescript";
import { isReactComponent, qualifiedName, testName, type Context } from "./extract.ts";
import { addPage, lazyTarget } from "./routes.ts";

/**
 * Screens with no route (the `root-screens` component, off by default): what a React app shows from
 * its root instead of its routes, worked out from the JSX alone. No component or folder name decides.
 *
 * - The root is what a `render`/`createRoot`/`hydrateRoot` call from react-dom mounts. Its outermost
 *   component is walked, not counted, unless it renders nothing of its own: then it's the only screen.
 * - A component on another branch from the routes (`token ? <Routes/> : <Sign/>`, or an early return)
 *   is shown instead of them: a screen.
 * - A component always shown beside the routes, or wrapped round them, is layout: not a screen.
 * - A component carrying routes in its own body is part of the path to them: walked, not counted.
 * - With no routes at all, every component the root shows counts: layout can't be told apart there.
 * - Shown by a condition beside the routes, or picked at runtime: ❓, never a guess.
 */
export const NO_ROUTE = "(no route) ";
const DETECTOR = "root-screens";
const ROUTE_TAGS = new Set(["Route", "Routes", "Switch", "RouterProvider"]);

interface Item { id: string; at: ts.Node; instead: boolean; conditional: boolean; walked: boolean }
interface Unknown { at: ts.Node; reason: string }
interface Scan { routes: boolean; items: Item[]; unknown: Unknown[] }

const empty = (): Scan => ({ routes: false, items: [], unknown: [] });

/** Shown together: beside routes, a component is layout, or ❓ when a condition shows it. */
function together(parts: Scan[]): Scan {
  const routes = parts.some((p) => p.routes);
  const items = parts.flatMap((p) => p.items);
  const unknown = parts.flatMap((p) => p.unknown);
  if (!routes) return { routes, items, unknown };
  for (const i of items)
    if (i.conditional && !i.instead) unknown.push({ at: i.at, reason: `${i.id} is shown by a condition beside the routes: a screen or a pop-up can't be told apart` });
  return { routes, items: items.filter((i) => i.instead), unknown };
}

/** Only one is shown: what's on a branch without routes is shown instead of them. */
function branches(parts: Scan[]): Scan {
  const routes = parts.some((p) => p.routes);
  for (const p of parts)
    for (const i of p.items) { i.conditional = true; if (routes && !p.routes) i.instead = true; }
  return { routes, items: parts.flatMap((p) => p.items), unknown: parts.flatMap((p) => p.unknown) };
}

export function rootScreens(ctx: Context, files: ts.SourceFile[]) {
  const scanner = new Scanner(ctx);
  const items: Item[] = [];
  const unknown: Unknown[] = [];
  for (const sf of files) {
    const visit = (n: ts.Node) => {
      const mounted = ts.isCallExpression(n) && !inTest(n) ? scanner.mounted(n) : null;
      if (mounted) {
        const s = scanner.mount(mounted);
        items.push(...s.items);
        unknown.push(...s.unknown);
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }

  const routed = new Set<string>();
  for (const l of ctx.out.lines) if (l.t === "edge" && l.kind === "routes-to") routed.add(l.target);
  const seen = new Map<string, Item>();
  for (const i of items) if (!routed.has(i.id) && !seen.has(i.id)) seen.set(i.id, i);
  const nameOf = (id: string) => {
    const name = id.slice(id.indexOf("#") + 1);
    return name === "default" ? id.slice(id.indexOf("|") + 1, id.indexOf("#")).replace(/^.*\//, "").replace(/\.[^.]+$/, "") : name;
  };
  const counts = new Map<string, number>();
  for (const id of seen.keys()) counts.set(nameOf(id), (counts.get(nameOf(id)) ?? 0) + 1);
  for (const [id, i] of seen) {
    const name = nameOf(id);
    const file = id.slice(id.indexOf("|") + 1, id.indexOf("#")).replace(/\.[^.]+$/, "");
    addPage(ctx, i.at, NO_ROUTE + (counts.get(name)! > 1 ? (file.endsWith(`/${name}`) ? file : `${file}/${name}`) : name), { ids: [id], unresolved: false });
  }

  const reported = new Set<string>();
  for (const u of unknown) {
    const file = ctx.rel(u.at.getSourceFile().fileName);
    const key = `${file}:${ctx.line(u.at)}:${u.reason}`;
    if (reported.has(key)) continue;
    reported.add(key);
    ctx.out.blind(ctx.ownerId(u.at), file, ctx.line(u.at), `no-route screen: ${u.reason}`, DETECTOR);
  }
}

function inTest(n: ts.Node): boolean {
  for (let p: ts.Node | undefined = n; p; p = p.parent) if (testName(p)) return true;
  return false;
}

class Scanner {
  private readonly bodies = new Map<ts.Node, Scan>();
  private readonly walking = new Set<ts.Node>();
  private readonly ctx: Context;

  constructor(ctx: Context) { this.ctx = ctx; }

  /** The element a react-dom mount call shows, or null when this call isn't one. */
  mounted(call: ts.CallExpression): ts.Expression | null {
    const callee = call.expression;
    const name = ts.isPropertyAccessExpression(callee) ? callee.name.text : ts.isIdentifier(callee) ? callee.text : "";
    if (name === "hydrateRoot" && this.fromReactDom(callee)) return call.arguments[1] ?? null;
    if (name !== "render" && name !== "hydrate") return null;
    if (ts.isIdentifier(callee)) return this.fromReactDom(callee) ? call.arguments[0] ?? null : null;
    if (!ts.isPropertyAccessExpression(callee)) return null;
    const on = callee.expression;
    if (this.fromReactDom(callee)) return call.arguments[0] ?? null;
    if (ts.isCallExpression(on) && this.createsRoot(on)) return call.arguments[0] ?? null;
    if (ts.isIdentifier(on)) {
      const d = this.ctx.checker.getSymbolAtLocation(on)?.valueDeclaration;
      if (d && ts.isVariableDeclaration(d) && d.initializer && ts.isCallExpression(d.initializer) && this.createsRoot(d.initializer)) return call.arguments[0] ?? null;
    }
    return null;
  }

  private createsRoot(call: ts.CallExpression): boolean {
    const c = call.expression;
    const name = ts.isPropertyAccessExpression(c) ? c.name.text : ts.isIdentifier(c) ? c.text : "";
    return name === "createRoot" && this.fromReactDom(c);
  }

  /** `render` or `ReactDOM.render`: the leftmost name was imported from react-dom. */
  private fromReactDom(e: ts.Expression): boolean {
    const base = ts.isPropertyAccessExpression(e) ? e.expression : e;
    if (!ts.isIdentifier(base)) return false;
    const specifier = importedFrom(this.ctx.checker.getSymbolAtLocation(base));
    return specifier !== null && /^react-dom(\/|$)/.test(specifier);
  }

  /** The screens under one mount: the outermost own components are the root, so they're walked. */
  mount(e: ts.Expression): Scan {
    const s = this.expr(e);
    if (!ts.isJsxElement(e) && !ts.isJsxSelfClosingElement(e) && !ts.isJsxFragment(e) && !ts.isParenthesizedExpression(e)
        && s.items.length === 0 && !s.routes && s.unknown.length === 0)
      s.unknown.push({ at: e, reason: "the app's root isn't written as JSX, so what it shows wasn't read" });
    const parts: Scan[] = [{ routes: s.routes, items: [], unknown: s.unknown }];
    for (const item of s.items) {
      if (item.walked) { parts[0].items.push(item); continue; }
      const inner = this.componentById(item);
      if (inner && (inner.routes || inner.items.length > 0 || inner.unknown.length > 0))
        parts.push({ routes: inner.routes, items: inner.items.map((i) => ({ ...i, walked: true })), unknown: inner.unknown });
      else parts[0].items.push(item);
    }
    return together(parts);
  }

  private readonly decls = new Map<string, ts.Node>();

  private componentById(item: Item): Scan | null {
    const decl = this.decls.get(item.id);
    return decl ? this.body(decl) : null;
  }

  /** What a component's body shows, each return being one branch. */
  private body(decl: ts.Node): Scan {
    const known = this.bodies.get(decl);
    if (known) return copy(known);
    if (this.walking.has(decl)) return empty();
    this.walking.add(decl);
    const fn = functionOf(decl);
    const results = fn ? returnsOf(fn).map((r) => this.expr(r)) : [];
    const scan = results.length > 1 ? branches(results) : results[0] ?? empty();
    this.walking.delete(decl);
    this.bodies.set(decl, scan);
    return copy(scan);
  }

  private expr(e: ts.Node): Scan {
    if (ts.isParenthesizedExpression(e) || ts.isJsxExpression(e) || ts.isAsExpression(e) || ts.isNonNullExpression(e)) {
      return e.expression ? this.expr(e.expression) : empty();
    }
    if (ts.isJsxFragment(e)) return this.children(e.children);
    if (ts.isJsxElement(e)) return this.element(e.openingElement.tagName, e, e.children);
    if (ts.isJsxSelfClosingElement(e)) return this.element(e.tagName, e, null);
    if (ts.isConditionalExpression(e)) return branches([this.expr(e.whenTrue), this.expr(e.whenFalse)]);
    if (ts.isBinaryExpression(e) && [ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(e.operatorToken.kind))
      return branches([this.expr(e.right)]);
    if (ts.isArrayLiteralExpression(e)) return together(e.elements.map((x) => this.expr(x)));
    if (ts.isElementAccessExpression(e)) return { routes: false, items: [], unknown: [{ at: e, reason: `${e.getText().slice(0, 60)} is picked at runtime` }] };
    if (ts.isCallExpression(e)) return this.call(e);
    if (ts.isIdentifier(e)) return this.variable(e);
    return empty();
  }

  private children(list: ts.NodeArray<ts.JsxChild>): Scan {
    return together(list.filter((c) => !ts.isJsxText(c)).map((c) => this.expr(c)));
  }

  private element(tag: ts.JsxTagNameExpression, at: ts.Node, kids: ts.NodeArray<ts.JsxChild> | null): Scan {
    const rightmost = ts.isPropertyAccessExpression(tag) ? tag.name.text : tag.getText();
    if (ROUTE_TAGS.has(rightmost)) return { routes: true, items: [], unknown: [] };
    const inner = kids ? this.children(kids) : empty();
    const hasKids = !!kids && kids.some((c) => !ts.isJsxText(c) || c.getText().trim().length > 0);
    if (ts.isIdentifier(tag) && /^[a-z]/.test(tag.text)) return inner;
    if (rightmost === "Provider" || rightmost === "Consumer") return inner;
    const r = this.resolve(tag);
    if (r.kind === "library") return inner;
    if (r.kind === "unknown") return together([inner, { routes: false, items: [], unknown: [{ at, reason: r.reason }] }]);
    const own = this.body(r.decl);
    this.decls.set(r.id, r.decl);
    if (own.routes) return together([inner, { routes: true, items: own.items.map((i) => ({ ...i, walked: true })), unknown: own.unknown }]);
    if (hasKids) return inner;
    return { routes: false, items: [{ id: r.id, at, instead: false, conditional: false, walked: false }], unknown: [] };
  }

  /** `{cases.map(c => <X/>)}` is shown by a condition; `{renderBody()}` is read when it's this project's own function. */
  private call(e: ts.CallExpression): Scan {
    const callbacks = e.arguments.filter((a) => (ts.isArrowFunction(a) || ts.isFunctionExpression(a)) && hasJsx(a));
    if (callbacks.length > 0) return branches(callbacks.map((c) => this.body(c)));
    let s = this.ctx.checker.getSymbolAtLocation(e.expression);
    if (s && s.flags & ts.SymbolFlags.Alias) s = this.ctx.checker.getAliasedSymbol(s);
    const d = s?.declarations?.find((x) => this.isOwn(x) && hasJsx(x));
    return d ? this.body(d) : empty();
  }

  /** `{view}`: what the variable is given. A prop or state holding an element has no such source, and is taken for text. */
  private variable(id: ts.Identifier): Scan {
    const d = this.ctx.checker.getSymbolAtLocation(id)?.valueDeclaration;
    if (!d || !ts.isVariableDeclaration(d) || !this.isOwn(d) || this.walking.has(d)) return empty();
    const sources: ts.Expression[] = d.initializer && hasJsx(d.initializer) ? [d.initializer] : [];
    let scope: ts.Node = d;
    while (scope.parent && !ts.isFunctionLike(scope) && !ts.isSourceFile(scope)) scope = scope.parent;
    const symbol = this.ctx.checker.getSymbolAtLocation(id);
    const visit = (n: ts.Node) => {
      if (ts.isBinaryExpression(n) && n.operatorToken.kind === ts.SyntaxKind.EqualsToken && ts.isIdentifier(n.left)
          && this.ctx.checker.getSymbolAtLocation(n.left) === symbol && hasJsx(n.right)) sources.push(n.right);
      ts.forEachChild(n, visit);
    };
    visit(scope);
    this.walking.add(d);
    const parts = sources.map((x) => this.expr(x));
    this.walking.delete(d);
    return parts.length > 1 ? branches(parts) : parts[0] ?? empty();
  }

  private isOwn(n: ts.Node) { return this.ctx.isOwn(n.getSourceFile()); }

  private resolve(tag: ts.JsxTagNameExpression): { kind: "library" } | { kind: "unknown"; reason: string } | { kind: "own"; decl: ts.Node; id: string } {
    const text = tag.getText();
    const located = this.ctx.checker.getSymbolAtLocation(tag);
    let symbol = located;
    if (symbol && symbol.flags & ts.SymbolFlags.Alias) symbol = this.ctx.checker.getAliasedSymbol(symbol);
    const own = (symbol?.declarations ?? []).filter((d) => this.isOwn(d));
    if (own.length === 0) {
      if ((symbol?.declarations ?? []).length > 0) return { kind: "library" };
      const base = ts.isPropertyAccessExpression(tag) ? leftmost(tag) : tag;
      const specifier = ts.isIdentifier(base) ? importedFrom(this.ctx.checker.getSymbolAtLocation(base)) : null;
      if (specifier !== null && !specifier.startsWith(".")) return { kind: "library" };
      return { kind: "unknown", reason: `<${text}> couldn't be resolved` };
    }
    for (const d of own) {
      if (ts.isExportAssignment(d) && ts.isIdentifier(d.expression)) {
        const next = this.ctx.symbolId(this.ctx.checker.getSymbolAtLocation(d.expression));
        const target = this.ctx.checker.getSymbolAtLocation(d.expression);
        const resolved = target && target.flags & ts.SymbolFlags.Alias ? this.ctx.checker.getAliasedSymbol(target) : target;
        const decl = resolved?.declarations?.find((x) => this.isOwn(x));
        if (next && decl && isComponentDecl(decl)) return { kind: "own", decl, id: next };
      }
      const id = this.ctx.declId(d);
      if (id && isComponentDecl(d)) return { kind: "own", decl: d, id };
      if (ts.isVariableDeclaration(d) && d.initializer) {
        if (ts.isTaggedTemplateExpression(d.initializer)) return { kind: "library" }; // a styled element
        if (ts.isCallExpression(d.initializer) && containsImport(d.initializer)) {
          const target = lazyTarget(this.ctx, d.initializer);
          const decl = target?.declarations?.find((x) => this.isOwn(x));
          const lazyId = this.ctx.symbolId(target);
          if (lazyId && decl) return this.lazyDecl(decl, lazyId, text);
        }
      }
    }
    return { kind: "unknown", reason: `<${text}> is picked at runtime or wrapped, so which component it shows wasn't resolved` };
  }

  private lazyDecl(decl: ts.Node, id: string, text: string): { kind: "unknown"; reason: string } | { kind: "own"; decl: ts.Node; id: string } {
    if (isComponentDecl(decl)) return { kind: "own", decl, id };
    if (ts.isExportAssignment(decl) && ts.isIdentifier(decl.expression)) {
      const target = this.ctx.checker.getSymbolAtLocation(decl.expression);
      const found = target?.declarations?.find((x) => isComponentDecl(x));
      if (found) return { kind: "own", decl: found, id };
    }
    return { kind: "unknown", reason: `<${text}> is loaded lazily and what it loads isn't a plain component` };
  }
}

function copy(s: Scan): Scan {
  return { routes: s.routes, items: s.items.map((i) => ({ ...i })), unknown: [...s.unknown] };
}

function leftmost(e: ts.PropertyAccessExpression): ts.Expression {
  let x: ts.Expression = e;
  while (ts.isPropertyAccessExpression(x)) x = x.expression;
  return x;
}

function importedFrom(symbol: ts.Symbol | undefined): string | null {
  for (const d of symbol?.declarations ?? []) {
    for (let p: ts.Node | undefined = d; p; p = p.parent)
      if (ts.isImportDeclaration(p) && ts.isStringLiteral(p.moduleSpecifier)) return p.moduleSpecifier.text;
  }
  return null;
}

function isComponentDecl(d: ts.Node): boolean {
  if (ts.isClassDeclaration(d)) return d.members.some((m) => ts.isMethodDeclaration(m) && ts.isIdentifier(m.name) && m.name.text === "render");
  if (ts.isFunctionDeclaration(d) && qualifiedName(d) === "default") return hasJsx(d);
  return isReactComponent(d);
}

function functionOf(decl: ts.Node): ts.FunctionLikeDeclaration | null {
  if (ts.isClassDeclaration(decl)) return (decl.members.find((m) => ts.isMethodDeclaration(m) && ts.isIdentifier(m.name) && m.name.text === "render") as ts.MethodDeclaration) ?? null;
  if (ts.isFunctionLike(decl) && "body" in decl) return decl as ts.FunctionLikeDeclaration;
  if (ts.isVariableDeclaration(decl) && decl.initializer) {
    if (ts.isArrowFunction(decl.initializer) || ts.isFunctionExpression(decl.initializer)) return decl.initializer;
    if (ts.isCallExpression(decl.initializer)) // memo(() => …), forwardRef(function …)
      return (decl.initializer.arguments.find((a) => ts.isArrowFunction(a) || ts.isFunctionExpression(a)) as ts.FunctionLikeDeclaration) ?? null;
  }
  return null;
}

/** What a function returns, without entering the functions inside it. */
function returnsOf(fn: ts.FunctionLikeDeclaration): ts.Expression[] {
  const body = fn.body;
  if (!body) return [];
  if (!ts.isBlock(body)) return [body];
  const found: ts.Expression[] = [];
  const visit = (n: ts.Node) => {
    if (ts.isFunctionLike(n) || ts.isClassLike(n)) return;
    if (ts.isReturnStatement(n) && n.expression) found.push(n.expression);
    ts.forEachChild(n, visit);
  };
  visit(body);
  return found;
}

function hasJsx(n: ts.Node): boolean {
  if (ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n) || ts.isJsxFragment(n)) return true;
  return ts.forEachChild(n, (c) => hasJsx(c) || undefined) ?? false;
}

function containsImport(n: ts.Node): boolean {
  if (ts.isCallExpression(n) && n.expression.kind === ts.SyntaxKind.ImportKeyword) return true;
  return ts.forEachChild(n, (c) => containsImport(c) || undefined) ?? false;
}

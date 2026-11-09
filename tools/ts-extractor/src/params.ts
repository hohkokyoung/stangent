import ts from "typescript";
import type { Context } from "./extract.ts";
import { BASE, RUNTIME, boundTo, isBaseName, stringValue } from "./values.ts";

/**
 * D190: the values a string can take when it's built from a function's parameters: each in-repo call of that function passes
 * its own argument, read the same way, through as many functions as pass it on. A value remembers where it was first given
 * (`origin`: the point whose call passes a constant), every point it passed through (`via`), and which side of each `?:` it
 * took (`when`, by the condition's text), so a verb and a URL written under the same condition pair up.
 */
export interface Valued { value: string; origin: string | null; via: string[]; when: Record<string, boolean>; open?: string }

const MAX = 40;   // more values than this: not followed
const DEPTH = 8;  // functions passed through

/** The values, when at least one comes from a caller's argument; null when the expression doesn't depend on one. */
export function paramValues(ctx: Context, expr: ts.Expression | undefined): Valued[] | null {
  const r = values(ctx, expr, new Map(), 0);
  return r && r.some((v) => v.origin !== null || v.open) ? dedupe(r) : null;
}

/** Every value an expression can take, read as {@link paramValues} does; null when the code shows none. */
export function valuesOf(ctx: Context, expr: ts.Expression | undefined): Valued[] | null {
  const r = values(ctx, expr, new Map(), 0);
  return r ? dedupe(r) : null;
}

/** Two values joined, or null when they took opposite sides of one condition. */
export function join(a: Valued, b: Valued): Valued | null {
  for (const [k, v] of Object.entries(b.when)) if (k in a.when && a.when[k] !== v) return null;
  return { value: a.value + b.value, origin: a.origin ?? b.origin, via: [...new Set([...a.via, ...b.via])], when: { ...a.when, ...b.when }, open: a.open ?? b.open };
}

const one = (value: string): Valued[] => [{ value, origin: null, via: [], when: {} }];

function dedupe(vs: Valued[]): Valued[] {
  const seen = new Map<string, Valued>();
  for (const v of vs) {
    const key = `${v.value}\u0000${v.origin}\u0000${v.open}\u0000${JSON.stringify(Object.entries(v.when).sort())}`;
    if (!seen.has(key)) seen.set(key, v);
  }
  return [...seen.values()];
}

function product(lists: Valued[][]): Valued[] | null {
  let acc: Valued[] = one("");
  for (const list of lists) {
    const next: Valued[] = [];
    for (const a of acc) for (const b of list) { const j = join(a, b); if (j) next.push(j); }
    if (next.length > MAX) return null;
    acc = next;
  }
  return acc;
}

function skip(e: ts.Expression): ts.Expression {
  while (ts.isParenthesizedExpression(e) || ts.isAsExpression(e) || ts.isNonNullExpression(e) || ts.isTypeAssertionExpression(e) || ts.isSatisfiesExpression(e))
    e = e.expression;
  return e;
}

function part(ctx: Context, e: ts.Expression, env: Map<ts.Symbol, Valued[]>, depth: number, leading: boolean): Valued[] {
  return values(ctx, e, env, depth + 1) ?? one(leading && isBaseName(e) ? BASE : RUNTIME);
}

function symbolOf(checker: ts.TypeChecker, n: ts.Node): ts.Symbol | undefined {
  let s = checker.getSymbolAtLocation(n);
  if (s && s.flags & ts.SymbolFlags.Alias) { try { s = checker.getAliasedSymbol(s); } catch { return undefined; } }
  return s;
}

function values(ctx: Context, expr: ts.Expression | undefined, env: Map<ts.Symbol, Valued[]>, depth: number): Valued[] | null {
  if (!expr || depth > 4 * DEPTH) return null;
  const e = skip(expr);
  if (ts.isStringLiteral(e) || ts.isNoSubstitutionTemplateLiteral(e) || ts.isNumericLiteral(e)) {
    const v = stringValue(e, ctx.checker);
    return v === null ? null : one(v);
  }
  if (ts.isConditionalExpression(e)) {
    const test = e.condition.getText();
    const side = (branch: ts.Expression, taken: boolean) =>
      (values(ctx, branch, env, depth + 1) ?? one(RUNTIME)).flatMap((v) => (test in v.when && v.when[test] !== taken ? [] : [{ ...v, when: { ...v.when, [test]: taken } }]));
    return [...side(e.whenTrue, true), ...side(e.whenFalse, false)];
  }
  if (ts.isTemplateExpression(e)) {
    const lists: Valued[][] = [one(e.head.text)];
    e.templateSpans.forEach((s, i) => { lists.push(part(ctx, s.expression, env, depth, i === 0 && e.head.text === "")); lists.push(one(s.literal.text)); });
    return product(lists);
  }
  if (ts.isBinaryExpression(e) && e.operatorToken.kind === ts.SyntaxKind.PlusToken)
    return product([part(ctx, e.left, env, depth, true), part(ctx, e.right, env, depth, false)]);
  if (ts.isIdentifier(e)) {
    const s = symbolOf(ctx.checker, e);
    if (s && env.has(s)) return env.get(s)!;
    const d = s?.valueDeclaration;
    if (d && ts.isParameter(d)) return fromCallers(ctx, d, null, depth);
    // `const { path } = options` / a parameter written `{ path }`: that property of what the callers pass.
    if (d && ts.isBindingElement(d) && ts.isObjectBindingPattern(d.parent) && ts.isParameter(d.parent.parent)) {
      const prop = d.propertyName && ts.isIdentifier(d.propertyName) ? d.propertyName.text : ts.isIdentifier(d.name) ? d.name.text : null;
      return prop ? fromCallers(ctx, d.parent.parent, prop, depth) : null;
    }
    const init = boundTo(e, ctx.checker);
    return init ? values(ctx, init, env, depth + 1) : null;
  }
  if (ts.isPropertyAccessExpression(e)) {
    // `options.path`, options a parameter: that property of what each caller passes.
    if (ts.isIdentifier(e.expression)) {
      const d = symbolOf(ctx.checker, e.expression)?.valueDeclaration;
      if (d && ts.isParameter(d)) return fromCallers(ctx, d, e.name.text, depth);
    }
    const init = boundTo(e, ctx.checker);
    if (init) return values(ctx, init, env, depth + 1);
    const v = stringValue(e, ctx.checker);
    return v === null ? null : one(v);
  }
  if (ts.isCallExpression(e)) {
    const fn = declaredFunction(ctx.checker, e.expression);
    if (!fn?.body) return null;
    if (queryString(ctx, fn)) return one("");
    const returned = returns(fn);
    if (returned.length === 0) return null;
    // A function is read with its parameters bound to this call's arguments (getQueryPath(path)); with several returns,
    // each is one value it can give (D193). `undefined`/`null` returns give none.
    const inner = new Map<ts.Symbol, Valued[]>();
    fn.parameters.forEach((p, i) => {
      const s = ts.isIdentifier(p.name) ? symbolOf(ctx.checker, p.name) : undefined;
      const arg = e.arguments[i] ?? p.initializer;
      if (s) inner.set(s, arg ? part(ctx, arg, env, depth, i === 0) : one(RUNTIME));
    });
    const out: Valued[] = [];
    for (const r of returned) {
      const vs = values(ctx, r, inner, depth + 1);
      if (!vs) return null;
      out.push(...vs);
      if (out.length > MAX) return null;
    }
    return out;
  }
  return null;
}

/** A function whose every return is "" or starts with "?": it adds a query string, which isn't part of the path. */
function queryString(ctx: Context, fn: ts.FunctionLikeDeclaration): boolean {
  if (!fn.body || !ts.isBlock(fn.body)) return false;
  const returns: ts.ReturnStatement[] = [];
  const visit = (n: ts.Node) => {
    if (ts.isReturnStatement(n)) returns.push(n);
    if (!ts.isFunctionLike(n)) ts.forEachChild(n, visit);
  };
  fn.body.statements.forEach(visit);
  return returns.length > 0 && returns.every((r) => {
    if (!r.expression) return false;
    const x = skip(r.expression);
    if (ts.isTemplateExpression(x)) return x.head.text.startsWith("?");
    const v = stringValue(x, ctx.checker);
    return v !== null && (v === "" || v.startsWith("?"));
  });
}

/** What a function returns: its expression body, or every `return`'s value outside nested functions, `undefined` and `null` left out. */
export function returns(fn: ts.FunctionLikeDeclaration): ts.Expression[] {
  if (!fn.body) return [];
  if (!ts.isBlock(fn.body)) return [fn.body];
  const out: ts.Expression[] = [];
  const visit = (n: ts.Node) => {
    if (ts.isReturnStatement(n)) {
      const x = n.expression ? skip(n.expression) : undefined;
      if (x && !(ts.isIdentifier(x) && x.text === "undefined") && x.kind !== ts.SyntaxKind.NullKeyword) out.push(n.expression!);
    }
    if (!ts.isFunctionLike(n)) ts.forEachChild(n, visit);
  };
  fn.body.statements.forEach(visit);
  return out;
}

/** Each in-repo call's argument for the parameter (or that property of it), read in the caller's own code, in file and line order. */
function fromCallers(ctx: Context, param: ts.ParameterDeclaration, prop: string | null, depth: number): Valued[] | null {
  if (depth > DEPTH * 4) return null;
  const fn = param.parent;
  if (!ts.isFunctionLike(fn)) return null;
  const index = fn.parameters.indexOf(param);
  const calls = callers(ctx).get(fn) ?? [];
  if (calls.length === 0) {
    // D191: no code in the repo's scripts calls it, so its caller is outside them (a view's onclick): the value is that
    // caller's argument, said as `open` (the point and the argument), for whoever knows the call to fill in.
    const point = pointOf(ctx, fn);
    return point ? [{ value: RUNTIME, origin: null, via: [], when: {}, open: `${point}@${index}${prop !== null ? `.${prop}` : ""}` }] : null;
  }
  const out: Valued[] = [];
  for (const c of calls) {
    let arg: ts.Expression | undefined = c.arguments[index] ?? param.initializer;
    if (arg && prop !== null) {
      const a = skip(arg);
      arg = ts.isObjectLiteralExpression(a) ? property(a, prop) : undefined;
    }
    const owner = ctx.ownerId(c);
    const vs = arg ? values(ctx, arg, new Map(), depth + 4) ?? one(RUNTIME) : one(RUNTIME);
    for (const v of vs)
      out.push({ value: v.value, origin: v.origin ?? (v.value.includes(RUNTIME) ? null : owner), via: [owner, ...v.via.filter((x) => x !== owner)], when: v.when, open: v.open });
    if (out.length > MAX) return null;
  }
  return out;
}

/** The point a function is: its own declaration, or the variable or object property it's written as. */
function pointOf(ctx: Context, fn: ts.SignatureDeclaration): string | null {
  return ctx.declId(fn) ?? (fn.parent && (ts.isVariableDeclaration(fn.parent) || ts.isPropertyAssignment(fn.parent)) ? ctx.declId(fn.parent) : null);
}

function property(o: ts.ObjectLiteralExpression, name: string): ts.Expression | undefined {
  for (const p of o.properties) {
    if (ts.isPropertyAssignment(p) && (ts.isIdentifier(p.name) || ts.isStringLiteral(p.name)) && p.name.text === name) return p.initializer;
    if (ts.isShorthandPropertyAssignment(p) && p.name.text === name) return p.name;
  }
  return undefined;
}

const callerIndex = new WeakMap<ts.Program, Map<ts.Node, ts.CallExpression[]>>();

/** Every call in the repo's own files by the function it calls, built once per program. */
function callers(ctx: Context): Map<ts.Node, ts.CallExpression[]> {
  const known = callerIndex.get(ctx.program);
  if (known) return known;
  const index = new Map<ts.Node, ts.CallExpression[]>();
  const files = ctx.program.getSourceFiles().filter((sf) => ctx.isOwn(sf)).sort((a, b) => (a.fileName < b.fileName ? -1 : a.fileName > b.fileName ? 1 : 0));
  for (const sf of files) {
    const visit = (n: ts.Node) => {
      if (ts.isCallExpression(n)) {
        const fn = declaredFunction(ctx.checker, n.expression);
        if (fn) { const list = index.get(fn) ?? []; list.push(n); index.set(fn, list); }
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }
  callerIndex.set(ctx.program, index);
  return index;
}

export function declaredFunction(checker: ts.TypeChecker, callee: ts.Expression): ts.FunctionLikeDeclaration | null {
  const symbol = symbolOf(checker, ts.isPropertyAccessExpression(callee) ? callee.name : callee);
  const d = symbol?.declarations?.length === 1 ? symbol.declarations[0] : null;
  if (!d) return null;
  if (ts.isFunctionDeclaration(d)) return d;
  if (ts.isVariableDeclaration(d) && d.initializer && (ts.isArrowFunction(d.initializer) || ts.isFunctionExpression(d.initializer))) return d.initializer;
  if ((ts.isPropertyAssignment(d) || ts.isPropertyDeclaration(d)) && d.initializer && (ts.isArrowFunction(d.initializer) || ts.isFunctionExpression(d.initializer))) return d.initializer;
  if (ts.isMethodDeclaration(d)) return d;
  return null;
}

import ts from "typescript";
import { qualifiedName, testName, type Context } from "./extract.ts";
import { declaredFunction, returns } from "./params.ts";
import { boundTo } from "./values.ts";

/** Library calls that register a function to run when an event comes, not now. */
const LISTENERS = new Set(["on", "once", "addEventListener", "addListener", "onmessage"]);

/**
 * D201: whether code at this node runs when its point runs. Code inside a function handed to another function runs then only
 * if that function calls it. One handed to the repo's own code that keeps it (stores it, never calls it, nor passes it on to
 * code that does) runs later, when something else calls it: `kept:<callee>`. One registered with a library's event listener
 * (`connection.on(…)`, `addEventListener`) runs when the event comes: `listener:<name>`. Library callbacks otherwise
 * (`map`, `forEach`, `subscribe`, `then`) run as part of this code: "". Stops at the point that owns the code.
 */
export function deferredOf(ctx: Context, node: ts.Node): string {
  for (let p: ts.Node | undefined = node.parent; p && !ts.isSourceFile(p); p = p.parent) {
    if (qualifiedName(p) || testName(p)) return "";
    if (!(ts.isArrowFunction(p) || ts.isFunctionExpression(p))) continue;
    // D202: a function written as an object's property (`{ readFunc: (v) => … }`) is stored there, to be called later, unless
    // the object goes straight to a library call that runs it (`subscribe({ next, error })`, `$.ajax({ success })`).
    if (ts.isPropertyAssignment(p.parent) && ts.isObjectLiteralExpression(p.parent.parent)) {
      // A top-level object's function (`var AjaxCart = { send_ajax: function () {…} }`) is a point of its own (D191).
      if (qualifiedName(p.parent)) return "";
      const obj = p.parent.parent;
      const holder = ts.isCallExpression(obj.parent) && obj.parent.arguments.includes(obj) ? obj.parent : null;
      if (!holder) return `kept:${p.parent.name.getText()}`;
      const fnOf = declaredFunction(ctx.checker, holder.expression);
      if (fnOf && ctx.isOwn(fnOf.getSourceFile())) return `kept:${p.parent.name.getText()}`;
      const stream = streamOf(ctx, holder);
      if (stream) return stream;
      continue;
    }
    if (!ts.isCallExpression(p.parent) || !p.parent.arguments.includes(p)) continue;
    const call = p.parent;
    const name = ts.isIdentifier(call.expression) ? call.expression.text : ts.isPropertyAccessExpression(call.expression) ? call.expression.name.text : "";
    const fn = declaredFunction(ctx.checker, call.expression);
    if (fn && fn.getSourceFile() && ctx.isOwn(fn.getSourceFile())) {
      if (!callsParam(ctx, fn, call.arguments.indexOf(p), 0, new Set())) return `kept:${name}`;
      continue;
    }
    if (LISTENERS.has(name)) return `listener:${name}`;
    const stream = streamOf(ctx, call);
    if (stream) return stream;
  }
  return "";
}

/**
 * D202: a callback given to a stream (`.subscribe(…)`, or an operator inside `.pipe(…)`) runs when the stream sends a value.
 * A stream that starts with a request (an HTTP call, or the repo's own function returning one) sends its answer as part of
 * this code: "". Any other (a form's `valueChanges`, a subject, a signal's observable, route data) sends values when they
 * come: `stream:<source>`. Not a stream callback: "".
 */
function streamOf(ctx: Context, call: ts.CallExpression): string {
  let streamCall: ts.CallExpression | null = null;
  if (ts.isPropertyAccessExpression(call.expression) && call.expression.name.text === "subscribe") streamCall = call;
  else if (ts.isCallExpression(call.parent) && ts.isPropertyAccessExpression(call.parent.expression) && call.parent.expression.name.text === "pipe"
           && call.parent.arguments.includes(call)) streamCall = call.parent;
  if (!streamCall) return "";
  const source = sourceOf((streamCall.expression as ts.PropertyAccessExpression).expression);
  if (isRequest(ctx, source, 0)) return "";
  // The source by its name: a call as `toObservable(…)`, anything else as written, on one line.
  const named = ts.isCallExpression(source) ? `${source.expression.getText()}(…)` : source.getText();
  return `stream:${named.replace(/\s+/g, " ").slice(0, 80)}`;
}

/** The start of a stream's chain: past `.pipe(…)` calls. */
function sourceOf(e: ts.Expression): ts.Expression {
  while (ts.isCallExpression(e) && ts.isPropertyAccessExpression(e.expression) && e.expression.name.text === "pipe") e = e.expression.expression;
  while (ts.isParenthesizedExpression(e)) e = e.expression;
  return e;
}

const VERBS = new Set(["get", "post", "put", "delete", "patch", "head", "request", "jsonp"]);

/** An HTTP call (`this.http.get(…)`, `httpClient.post(…)`), a `forkJoin`/`combineLatest` of them, or the repo's function returning one. */
function isRequest(ctx: Context, e: ts.Expression, depth: number): boolean {
  if (depth > 4) return false;
  e = sourceOf(e);
  if (ts.isIdentifier(e) || ts.isPropertyAccessExpression(e) && !ts.isCallExpression(e.parent)) {
    const init = ts.isIdentifier(e) || ts.isPropertyAccessExpression(e) ? boundTo(e, ctx.checker) : null;
    return init ? isRequest(ctx, init, depth + 1) : false;
  }
  if (!ts.isCallExpression(e)) return false;
  const callee = e.expression;
  const name = ts.isIdentifier(callee) ? callee.text : ts.isPropertyAccessExpression(callee) ? callee.name.text : "";
  if (ts.isPropertyAccessExpression(callee) && VERBS.has(name)) {
    const type = ctx.checker.getTypeAtLocation(callee.expression).getSymbol()?.getName();
    if (type === "HttpClient" || (!ctx.typed && /http/i.test(callee.expression.getText()))) return true;
  }
  if ((name === "forkJoin" || name === "combineLatest" || name === "zip") && e.arguments.length > 0) {
    const args = e.arguments.length === 1 && ts.isArrayLiteralExpression(e.arguments[0]) ? [...e.arguments[0].elements]
      : e.arguments.length === 1 && ts.isObjectLiteralExpression(e.arguments[0]) ? e.arguments[0].properties.filter(ts.isPropertyAssignment).map((p) => p.initializer) : [...e.arguments];
    return args.length > 0 && args.every((a) => isRequest(ctx, a, depth + 1));
  }
  const fn = declaredFunction(ctx.checker, callee);
  return !!fn && ctx.isOwn(fn.getSourceFile()) && returns(fn).some((r) => isRequest(ctx, r, depth + 1));
}

/** The function calls its parameter at this index, or passes it on to code that may (a library's, or the repo's that does). */
function callsParam(ctx: Context, fn: ts.SignatureDeclaration, index: number, depth: number, seen: Set<ts.Node>): boolean {
  if (depth > 6) return true;           // too deep to read: it may run
  if (seen.has(fn)) return false;       // passed back into a function already being read (recursion): nothing new
  seen.add(fn);
  const param = fn.parameters[index];
  const body = (fn as ts.FunctionLikeDeclaration).body;
  if (!param || !ts.isIdentifier(param.name) || !body) return true;   // a rest parameter or a pattern: not read, so it may run
  const symbol = ctx.checker.getSymbolAtLocation(param.name);
  let found = false;
  const visit = (n: ts.Node) => {
    if (found) return;
    if (ts.isIdentifier(n) && ctx.checker.getSymbolAtLocation(n) === symbol && n !== param.name) {
      const p = n.parent;
      if (ts.isCallExpression(p) && p.expression === n) found = true;
      else if (ts.isPropertyAccessExpression(p) && p.expression === n && ["call", "apply"].includes(p.name.text)) found = true;
      else if (ts.isCallExpression(p) && p.arguments.includes(n as ts.Expression)) {
        const next = declaredFunction(ctx.checker, p.expression);
        found = next && ctx.isOwn(next.getSourceFile()) ? callsParam(ctx, next, p.arguments.indexOf(n as ts.Expression), depth + 1, seen) : true;
      }
    }
    ts.forEachChild(n, visit);
  };
  visit(body);
  return found;
}

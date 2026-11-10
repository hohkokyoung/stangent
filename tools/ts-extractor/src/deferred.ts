import ts from "typescript";
import { qualifiedName, testName, type Context } from "./extract.ts";
import { declaredFunction } from "./params.ts";

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
    if (!(ts.isArrowFunction(p) || ts.isFunctionExpression(p)) || !ts.isCallExpression(p.parent) || !p.parent.arguments.includes(p)) continue;
    const call = p.parent;
    const name = ts.isIdentifier(call.expression) ? call.expression.text : ts.isPropertyAccessExpression(call.expression) ? call.expression.name.text : "";
    const fn = declaredFunction(ctx.checker, call.expression);
    if (fn && fn.getSourceFile() && ctx.isOwn(fn.getSourceFile())) {
      if (!callsParam(ctx, fn, call.arguments.indexOf(p), 0, new Set())) return `kept:${name}`;
      continue;
    }
    if (LISTENERS.has(name)) return `listener:${name}`;
  }
  return "";
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

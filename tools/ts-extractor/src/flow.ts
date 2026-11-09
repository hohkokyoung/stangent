import ts from "typescript";
import { isAngularComponent, qualifiedName, testName, type Context } from "./extract.ts";

/**
 * Facts layer 3 reads to lay out flows (D171, D174, D176), all by code:
 * - `on-load` (detector `on-load`): what runs as a screen appears. Every React effect runs on first render; one with
 *   dependencies runs again when they change, one with none after every render. A component's own hooks (`use…`) run on
 *   every render. A class component's `componentDidMount`, an Angular component's `ngOnInit`. An unusual shape is ❓.
 * - `condition` (detector `conditions`): per call under at least one `if`/`else`/`case`/`catch`/loop/`?:` of its own
 *   function, those conditions innermost first, their code quoted exactly.
 * - `exit` (detector `conditions`): a `throw`, with the conditions around it.
 */
const ON_LOAD = "on-load";
const CONDITIONS = "conditions";
export const EFFECT_HOOKS = new Set(["useEffect", "useLayoutEffect", "useInsertionEffect"]);

const calleeName = (c: ts.CallExpression) =>
  ts.isIdentifier(c.expression) ? c.expression.text : ts.isPropertyAccessExpression(c.expression) ? c.expression.name.text : "";

/** The component or hook whose own body a block is: `function X() {…}`, `const X = () => {…}`, `const X = memo(() => {…})`. */
function bodyOwner(block: ts.Node): string | null {
  if (!ts.isBlock(block)) return null;
  const fn = block.parent;
  let name: string | null = null;
  if (ts.isFunctionDeclaration(fn)) name = qualifiedName(fn);
  else if (ts.isArrowFunction(fn) || ts.isFunctionExpression(fn)) {
    const holder = ts.isCallExpression(fn.parent) ? fn.parent.parent : fn.parent;
    if (holder && ts.isVariableDeclaration(holder)) name = qualifiedName(holder);
  }
  return name && !name.includes(".") && (/^[A-Z]/.test(name) || /^use[A-Z0-9]/.test(name)) ? name : null;
}

/**
 * D176: an effect's function is a point of its own, `Owner.effectN` (N counts the effects written directly in the
 * component's or hook's body, from 1), so the calls it makes are told apart from the ones made while rendering.
 */
export function effectName(decl: ts.Node): string | null {
  if (!ts.isArrowFunction(decl) && !ts.isFunctionExpression(decl)) return null;
  const call = decl.parent;
  if (!call || !ts.isCallExpression(call) || call.arguments[0] !== decl || !EFFECT_HOOKS.has(calleeName(call))) return null;
  const statement = call.parent;
  if (!statement || !ts.isExpressionStatement(statement)) return null;
  const owner = bodyOwner(statement.parent);
  if (!owner) return null;
  let n = 0;
  for (const s of (statement.parent as ts.Block).statements) {
    if (ts.isExpressionStatement(s) && ts.isCallExpression(s.expression) && EFFECT_HOOKS.has(calleeName(s.expression))) n++;
    if (s === statement) return `${owner}.effect${n}`;
  }
  return null;
}

export function onLoadFacts(ctx: Context, sf: ts.SourceFile) {
  const file = ctx.rel(sf.fileName);
  const fact = (owner: string, at: ts.Node, data: Record<string, string>) => ctx.out.fact(ON_LOAD, "on-load", owner, file, ctx.line(at), data);
  const pointOf = (e: ts.Expression): string => {
    let s = ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(e) ? e.name : e);
    if (s && s.flags & ts.SymbolFlags.Alias) {
      try { s = ctx.checker.getAliasedSymbol(s); } catch { /* keep it */ }
    }
    return ctx.symbolId(s) ?? "";
  };
  const visit = (n: ts.Node) => {
    if (ts.isCallExpression(n) && EFFECT_HOOKS.has(calleeName(n))) effect(n);
    if (ts.isBlock(n) && bodyOwner(n) && /^[A-Z]/.test(bodyOwner(n)!)) hooks(n);
    if (ts.isClassDeclaration(n)) classHooks(n);
    ts.forEachChild(n, visit);
  };
  const effect = (call: ts.CallExpression) => {
    const hook = calleeName(call);
    const owner = ctx.ownerId(call);
    const fn = call.arguments[0];
    const statement = call.parent;
    if (!fn || !ts.isExpressionStatement(statement) || !bodyOwner(statement.parent)) {
      ctx.out.blind(owner, file, ctx.line(call), `${hook} not written directly in a component's or hook's body: when it runs isn't read`, ON_LOAD);
      return;
    }
    const handler = ts.isArrowFunction(fn) || ts.isFunctionExpression(fn) ? ctx.declId(fn) ?? "" : pointOf(fn);
    if (!handler) {
      ctx.out.blind(owner, file, ctx.line(call), `${hook} runs a function the graph can't follow: ${fn.getText(sf)}`, ON_LOAD);
      return;
    }
    const depsArg = call.arguments[1];
    let deps = "";
    let runs: string;
    if (!depsArg) runs = "first render, then after every render";
    else if (ts.isArrayLiteralExpression(depsArg)) {
      deps = depsArg.elements.map((e) => e.getText(sf)).join("|");
      runs = depsArg.elements.length === 0 ? "first render" : "first render, then again when a dependency changes";
    } else {
      ctx.out.blind(owner, file, ctx.line(call), `${hook} dependencies not written as a list: ${depsArg.getText(sf)}`, ON_LOAD);
      return;
    }
    fact(owner, call, { framework: "react", hook, handler, deps, runs });
  };
  // A component's own hooks, called while it renders: they run on every render, the first included.
  const hooks = (body: ts.Block) => {
    const owner = ctx.declId(body.parent.parent && ts.isVariableDeclaration(body.parent.parent) ? body.parent.parent
      : ts.isCallExpression(body.parent.parent) && ts.isVariableDeclaration(body.parent.parent.parent) ? body.parent.parent.parent : body.parent);
    if (!owner) return;
    const seen = new Set<string>();
    const walk = (n: ts.Node) => {
      if (ts.isArrowFunction(n) || ts.isFunctionExpression(n) || ts.isFunctionDeclaration(n) || ts.isClassLike(n)) return;
      if (ts.isCallExpression(n) && /^use[A-Z0-9]/.test(calleeName(n)) && !EFFECT_HOOKS.has(calleeName(n))) {
        const id = pointOf(n.expression);
        if (id && !seen.has(id)) {
          seen.add(id);
          fact(owner, n, { framework: "react", hook: calleeName(n), handler: id, deps: "", runs: "first render, then every render" });
        }
      }
      ts.forEachChild(n, walk);
    };
    body.statements.forEach(walk);
  };
  const classHooks = (cls: ts.ClassDeclaration) => {
    const owner = ctx.declId(cls);
    if (!owner) return;
    const angular = isAngularComponent(cls) || (ts.getDecorators(cls) ?? []).some((d) => ts.isCallExpression(d.expression)
      && ts.isIdentifier(d.expression.expression) && d.expression.expression.text === "Directive");
    const react = (cls.heritageClauses ?? []).some((h) => h.token === ts.SyntaxKind.ExtendsKeyword
      && h.types.some((t) => /(^|\.)(Pure)?Component$/.test(t.expression.getText(sf))));
    const wanted = angular ? "ngOnInit" : react ? "componentDidMount" : null;
    if (!wanted) return;
    for (const m of cls.members) {
      if (!ts.isMethodDeclaration(m) || !m.name || m.name.getText(sf) !== wanted) continue;
      const handler = ctx.declId(m);
      if (handler) fact(owner, m, { framework: angular ? "angular" : "react", hook: wanted, handler, deps: "", runs: "first render" });
    }
  };
  visit(sf);
}

/** One condition around a call: its kind and its code, quoted exactly. */
interface Condition { kind: string; text: string; line: number; case?: string }

/** D174: the conditions of its own function around a node, innermost first; stops at the point that owns it. */
export function conditionsAround(ctx: Context, node: ts.Node): Condition[] {
  const sf = node.getSourceFile();
  const out: Condition[] = [];
  let child: ts.Node = node;
  for (let p: ts.Node | undefined = node.parent; p && !ts.isSourceFile(p); child = p, p = p.parent) {
    if (qualifiedName(p) || testName(p)) break;
    const at = (n: ts.Node) => ctx.line(n);
    if (ts.isIfStatement(p)) {
      if (child === p.thenStatement) out.push({ kind: "if", text: p.expression.getText(sf), line: at(p) });
      else if (child === p.elseStatement) out.push({ kind: "else", text: p.expression.getText(sf), line: at(p) });
    } else if (ts.isConditionalExpression(p)) {
      if (child === p.whenTrue) out.push({ kind: "if", text: p.condition.getText(sf), line: at(p) });
      else if (child === p.whenFalse) out.push({ kind: "else", text: p.condition.getText(sf), line: at(p) });
    } else if (ts.isCaseClause(p) || ts.isDefaultClause(p)) {
      const sw = p.parent.parent as ts.SwitchStatement;
      if (p.statements.some((s) => s === child))
        out.push({ kind: "case", text: sw.expression.getText(sf), line: at(p), case: ts.isCaseClause(p) ? p.expression.getText(sf) : "default" });
    } else if (ts.isCatchClause(p)) {
      if (child === p.block) out.push({ kind: "catch", text: p.variableDeclaration?.getText(sf) ?? "", line: at(p) });
    } else if (ts.isForStatement(p) || ts.isForOfStatement(p) || ts.isForInStatement(p) || ts.isWhileStatement(p) || ts.isDoStatement(p)) {
      if (child === p.statement) out.push({ kind: "loop", text: loopHeader(p, sf), line: at(p) });
    }
  }
  return out;
}

function loopHeader(p: ts.IterationStatement, sf: ts.SourceFile): string {
  if (ts.isDoStatement(p)) return `do … while (${p.expression.getText(sf)})`;
  return sf.text.slice(p.getStart(sf), p.statement.getStart(sf)).trim();
}

export function conditionFacts(ctx: Context, sf: ts.SourceFile) {
  const file = ctx.rel(sf.fileName);
  const visit = (n: ts.Node) => {
    if (ts.isCallExpression(n) || ts.isNewExpression(n)) {
      const conditions = conditionsAround(ctx, n);
      if (conditions.length > 0) {
        const callee = n.expression;
        let s = ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(callee) ? callee.name : callee);
        if (s && s.flags & ts.SymbolFlags.Alias) {
          try { s = ctx.checker.getAliasedSymbol(s); } catch { /* keep it */ }
        }
        ctx.out.fact(CONDITIONS, "condition", ctx.ownerId(n), file, ctx.line(n), {
          call: callee.getText(sf), target: ctx.symbolId(s) ?? "", conditions: JSON.stringify(conditions),
        });
      }
    }
    if (ts.isThrowStatement(n)) {
      const thrown = n.expression;
      const exception = ts.isNewExpression(thrown) ? thrown.expression.getText(sf) : "";
      ctx.out.fact(CONDITIONS, "exit", ctx.ownerId(n), file, ctx.line(n), {
        kind: "throw", result: "throw", status: "", text: n.getText(sf), exception, conditions: JSON.stringify(conditionsAround(ctx, n)),
      });
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

import ts from "typescript";
import type { Context } from "./extract.ts";
import { boundTo, RUNTIME, BASE, stringValue } from "./values.ts";

/** Calls whose first argument is a text key: translate('AddSeries'), t('add'), i18n.t('x'), formatMessage({ id }). */
export const TEXT_CALLS = new Set(["translate", "t", "i18n", "formatMessage", "$t"]);
/** Attributes whose value is what the user reads or hears. */
export const LABEL_ATTRS = new Set(["label", "title", "aria-label", "text", "placeholder", "alt", "tooltip", "ngbTooltip", "matTooltip"]);

/**
 * G5 + G6: every JSX event attribute (`onPress={handleAddSeriesPress}`, `onClick={() => save()}`) is a `ui-event`
 * fact on its component: the element, the event, the handler it runs (a point, when it resolves) and what the
 * element shows: literal text and text keys (`translate('AddSeriesWithTitle')`), from its children and label attributes.
 */
export function reactEvents(ctx: Context, sf: ts.SourceFile) {
  const visit = (n: ts.Node) => {
    if (ts.isJsxOpeningElement(n) || ts.isJsxSelfClosingElement(n)) {
      const tag = n.tagName.getText(sf);
      const element = ts.isJsxOpeningElement(n) ? n.parent : n;
      for (const a of n.attributes.properties) {
        if (!ts.isJsxAttribute(a) || !ts.isIdentifier(a.name) || !/^on[A-Z]/.test(a.name.text)) continue;
        const expr = a.initializer && ts.isJsxExpression(a.initializer) ? a.initializer.expression : undefined;
        if (!expr) continue;
        const { keys, texts } = labelOf(element, sf);
        ctx.out.fact("ui", "ui-event", ctx.ownerId(a), ctx.rel(sf.fileName), ctx.line(a), {
          element: tag, event: a.name.text, handler: handlerOf(ctx, expr), keys: keys.join("|"), text: texts.join(" "),
        });
      }
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

/** The point an event runs: `handleX` or `this.x` → its id; `() => save()` → the one function it calls, else "inline". */
function handlerOf(ctx: Context, expr: ts.Expression): string {
  const id = (e: ts.Expression) => {
    let s = ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(e) ? e.name : e);
    if (s && s.flags & ts.SymbolFlags.Alias) {
      try { s = ctx.checker.getAliasedSymbol(s); } catch { /* keep it */ }
    }
    return ctx.symbolId(s);
  };
  if (ts.isIdentifier(expr) || ts.isPropertyAccessExpression(expr)) return id(expr) ?? `prop:${expr.getText()}`;
  if (ts.isArrowFunction(expr) || ts.isFunctionExpression(expr)) {
    const calls: string[] = [];
    const walk = (n: ts.Node) => {
      if (ts.isCallExpression(n)) { const c = id(n.expression); if (c && !calls.includes(c)) calls.push(c); }
      ts.forEachChild(n, walk);
    };
    walk(expr.body);
    return calls.length === 1 ? calls[0] : "inline";
  }
  return "inline";
}

/** An element holding other elements with events of their own is a container: only its own label attributes speak for it. */
function isContainer(element: ts.Node): boolean {
  let found = false;
  const visit = (n: ts.Node) => {
    if (found) return;
    if (n !== element && ts.isJsxAttribute(n) && ts.isIdentifier(n.name) && /^on[A-Z]/.test(n.name.text)
        && !(ts.isJsxSelfClosingElement(element) ? element.attributes : (element as ts.JsxElement).openingElement.attributes).properties.includes(n)) found = true;
    ts.forEachChild(n, visit);
  };
  visit(element);
  return found;
}

function labelOf(element: ts.Node, sf: ts.SourceFile): { keys: string[]; texts: string[] } {
  if (isContainer(element)) element = ts.isJsxElement(element) ? element.openingElement : element;
  const keys: string[] = [], texts: string[] = [];
  const add = (list: string[], v: string) => { v = v.replace(/\s+/g, " ").trim(); if (v && !list.includes(v)) list.push(v); };
  const visit = (n: ts.Node) => {
    if (ts.isJsxText(n)) add(texts, n.text);
    if (ts.isCallExpression(n)) {
      const callee = ts.isPropertyAccessExpression(n.expression) ? n.expression.name.text : ts.isIdentifier(n.expression) ? n.expression.text : "";
      const first = n.arguments[0];
      if (TEXT_CALLS.has(callee) && first && (ts.isStringLiteral(first) || ts.isNoSubstitutionTemplateLiteral(first))) add(keys, first.text);
    }
    if (ts.isJsxAttribute(n) && ts.isIdentifier(n.name) && LABEL_ATTRS.has(n.name.text) && n.initializer && ts.isStringLiteral(n.initializer))
      add(texts, n.initializer.text);
    // A nested element with its own event speaks for itself.
    if (n !== element && (ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n))) {
      const attrs = ts.isJsxElement(n) ? n.openingElement.attributes : n.attributes;
      if (attrs.properties.some((a) => ts.isJsxAttribute(a) && ts.isIdentifier(a.name) && /^on[A-Z]/.test(a.name.text))) return;
    }
    ts.forEachChild(n, visit);
  };
  visit(element);
  void sf;
  return { keys, texts };
}

/**
 * G10: `connection.on('receiveMessage', handler)` on a SignalR HubConnection is a `signalr-on` fact on the code that
 * listens, one per message name. A name from a loop over a known list (`events.forEach(e => connection.on(e, …))`,
 * `for (const e of [...])`) gives one fact per item; a name known only at runtime gives a fact with no name (❓ later).
 */
export function signalrListeners(ctx: Context, sf: ts.SourceFile) {
  const visit = (n: ts.Node) => {
    if (ts.isCallExpression(n) && ts.isPropertyAccessExpression(n.expression) && n.expression.name.text === "on" && n.arguments.length >= 2
        && isHubConnection(ctx, n.expression.expression)) {
      const names = namesOf(ctx, n.arguments[0]);
      for (const name of names.length > 0 ? names : [""])
        ctx.out.fact("frontend-http", "signalr-on", ctx.ownerId(n), ctx.rel(sf.fileName), ctx.line(n), { name });
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

function isHubConnection(ctx: Context, receiver: ts.Expression): boolean {
  const type = ctx.checker.getTypeAtLocation(receiver);
  const name = type.getSymbol()?.getName() ?? type.aliasSymbol?.getName() ?? "";
  if (name === "HubConnection") return true;
  // Without packages the type is unknown: go by the receiver's name.
  return (name === "" || name === "__type" || (type.flags & ts.TypeFlags.Any) !== 0) && /(hub)?connection(\.current)?$/i.test(receiver.getText());
}

const known = (v: string | null): v is string => v !== null && !v.includes(RUNTIME) && !v.includes(BASE);

function namesOf(ctx: Context, e: ts.Expression): string[] {
  const direct = stringValue(e, ctx.checker);
  if (known(direct)) return [direct];
  if (!ts.isIdentifier(e)) return [];
  const symbol = ctx.checker.getSymbolAtLocation(e);
  const d = symbol?.declarations?.[0];
  if (!d) return [];
  let list: ts.Expression | undefined;
  // events.forEach(event => …): the list forEach runs over.
  if (ts.isParameter(d) && (ts.isArrowFunction(d.parent) || ts.isFunctionExpression(d.parent)) && d.parent.parameters[0] === d
      && ts.isCallExpression(d.parent.parent) && ts.isPropertyAccessExpression(d.parent.parent.expression) && d.parent.parent.expression.name.text === "forEach")
    list = d.parent.parent.expression.expression;
  // for (const event of list)
  if (ts.isVariableDeclaration(d) && ts.isVariableDeclarationList(d.parent) && ts.isForOfStatement(d.parent.parent)) list = d.parent.parent.expression;
  if (list && (ts.isIdentifier(list) || ts.isPropertyAccessExpression(list))) list = boundTo(list, ctx.checker) ?? list;
  if (!list || !ts.isArrayLiteralExpression(list)) return [];
  const names = list.elements.map((x) => stringValue(x, ctx.checker));
  return names.every(known) ? names as string[] : [];
}

/** G9: a top-level `const X = 'literal'` (or number, boolean) is a `value` fact on its point. */
export function constantValues(ctx: Context, sf: ts.SourceFile) {
  for (const st of sf.statements) {
    if (!ts.isVariableStatement(st) || !(st.declarationList.flags & ts.NodeFlags.Const)) continue;
    for (const d of st.declarationList.declarations) {
      if (!ts.isIdentifier(d.name) || !d.initializer) continue;
      const init = ts.isAsExpression(d.initializer) ? d.initializer.expression : d.initializer;
      if (!(ts.isStringLiteral(init) || ts.isNoSubstitutionTemplateLiteral(init) || ts.isNumericLiteral(init) || ts.isTemplateExpression(init)
            || ts.isBinaryExpression(init) || init.kind === ts.SyntaxKind.TrueKeyword || init.kind === ts.SyntaxKind.FalseKeyword
            || (ts.isPrefixUnaryExpression(init) && ts.isNumericLiteral(init.operand)))) continue;
      const value = ts.isPrefixUnaryExpression(init) ? (init.operator === ts.SyntaxKind.MinusToken ? "-" : "") + (init.operand as ts.NumericLiteral).text
        : stringValue(init, ctx.checker);
      const id = ctx.declId(d);
      if (id && known(value)) ctx.out.fact("constants", "value", id, ctx.rel(sf.fileName), ctx.line(d), { value, kind: "const" });
    }
  }
}

const JQUERY_EVENTS = new Set(["click", "submit", "change", "input", "keyup", "keydown", "dblclick", "focus", "blur"]);

/**
 * N5: plain JavaScript's event bindings, as `dom-event` facts: `$('#id').on('click', fn)`, `$('#id').click(fn)`,
 * `$(document).on('click', '#id', fn)`, `document.getElementById('id').addEventListener('click', fn)`. The selector is
 * what joins them to a server-rendered view's buttons; the handler is a point when it resolves (as for React).
 */
export function domEvents(ctx: Context, sf: ts.SourceFile) {
  const value = (e: ts.Expression | undefined) => (e ? stringValue(e, ctx.checker) : null);
  const jquery = (e: ts.Expression): string | null => {
    if (!ts.isCallExpression(e) || !ts.isIdentifier(e.expression) || (e.expression.text !== "$" && e.expression.text !== "jQuery")) return null;
    const arg = e.arguments[0];
    if (!arg) return null;
    if (ts.isIdentifier(arg) && (arg.text === "document" || arg.text === "window")) return arg.text;
    const v = value(arg);
    return v && v !== RUNTIME && !v.includes("{*}") ? v : null;
  };
  const dom = (e: ts.Expression): string | null => {
    if (!ts.isCallExpression(e) || !ts.isPropertyAccessExpression(e.expression)) return null;
    const by = e.expression.name.text;
    const v = value(e.arguments[0]);
    if (!v || v === RUNTIME || v.includes("{*}")) return null;
    return by === "getElementById" ? `#${v}` : by === "querySelector" || by === "querySelectorAll" ? v : null;
  };
  const visit = (n: ts.Node) => {
    if (ts.isCallExpression(n) && ts.isPropertyAccessExpression(n.expression)) {
      const name = n.expression.name.text;
      const target = n.expression.expression;
      let selector: string | null = null;
      let event: string | null = null;
      let handler: ts.Expression | undefined;
      const sel = jquery(target);
      if (sel !== null) {
        if (["on", "one", "bind", "delegate"].includes(name) && n.arguments.length >= 2) {
          event = value(n.arguments[0]);
          const delegated = n.arguments.length >= 3 ? value(n.arguments[1]) : null;
          selector = delegated && delegated !== RUNTIME ? delegated : sel;
          handler = n.arguments[n.arguments.length - 1];
        } else if (JQUERY_EVENTS.has(name) && n.arguments.length >= 1) {
          event = name;
          selector = sel;
          handler = n.arguments[n.arguments.length - 1];
        }
      } else if (name === "addEventListener") {
        selector = dom(target) ?? (ts.isIdentifier(target) && (target.text === "document" || target.text === "window") ? target.text : null);
        event = value(n.arguments[0]);
        handler = n.arguments[1];
      }
      if (selector && event && event !== RUNTIME && handler) {
        ctx.out.fact("ui", "dom-event", ctx.ownerId(n), ctx.rel(sf.fileName), ctx.line(n), {
          selector, event: event.split(".")[0], handler: scriptHandlerOf(ctx, handler),
          cancels: cancels(handler, sel !== null) ? "yes" : "",
        });
      }
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

/**
 * V2: the handler always stops the browser's own action (a submit button's submit): a statement of its own body, not one
 * inside an `if` or a nested function, calls `preventDefault()` or, for jQuery (which honours it), returns `false`.
 */
function cancels(handler: ts.Expression, jquery: boolean): boolean {
  if (!ts.isArrowFunction(handler) && !ts.isFunctionExpression(handler)) return false;
  if (!ts.isBlock(handler.body)) return jquery && handler.body.kind === ts.SyntaxKind.FalseKeyword;
  return handler.body.statements.some((st) =>
    (jquery && ts.isReturnStatement(st) && st.expression?.kind === ts.SyntaxKind.FalseKeyword)
    || (ts.isExpressionStatement(st) && ts.isCallExpression(st.expression) && ts.isPropertyAccessExpression(st.expression.expression)
      && st.expression.expression.name.text === "preventDefault"));
}

/**
 * T7: a plain script's handler is a function only when it is one, or its body is that single call; anything more is `inline`.
 * jQuery calls don't resolve, so "the one repo function it calls" would pick a helper out of a body that does much more.
 */
function scriptHandlerOf(ctx: Context, expr: ts.Expression): string {
  if (!ts.isArrowFunction(expr) && !ts.isFunctionExpression(expr)) return handlerOf(ctx, expr);
  let only: ts.Expression | undefined;
  if (!ts.isBlock(expr.body)) only = expr.body;
  else if (expr.body.statements.length === 1) {
    const st = expr.body.statements[0];
    only = ts.isExpressionStatement(st) ? st.expression : ts.isReturnStatement(st) ? st.expression : undefined;
  }
  if (!only || !ts.isCallExpression(only)) return "inline";
  const callee = only.expression;
  return ts.isIdentifier(callee) || ts.isPropertyAccessExpression(callee) ? (handlerOf(ctx, callee).startsWith("prop:") ? "inline" : handlerOf(ctx, callee)) : "inline";
}

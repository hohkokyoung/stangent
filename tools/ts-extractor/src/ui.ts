import ts from "typescript";
import type { Context } from "./extract.ts";

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

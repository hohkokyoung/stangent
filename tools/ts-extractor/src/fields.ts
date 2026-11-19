// D329: `form-field` facts. Every control a user can type into, tick or pick from, on the template or component holding it:
// what it is bound to, its label and help keys (or words), and its choices. No rule names an app's own components: a component
// counts as a field (a "wrapper") only by what its own template does with a value it is given.
import ts from "typescript";
import { parseTemplate } from "@angular/compiler";
import type { Context } from "./extract.ts";
import { TEXT_CALLS } from "./ui.ts";
import { translocoScopes } from "./menu.ts";
import { scoped } from "./angular.ts";
import { RUNTIME, BASE, stringValue } from "./values.ts";

/** The browser's own controls. */
const NATIVE = new Set(["input", "select", "textarea"]);
/** Input types that aren't fields: buttons, and values the user never sees. */
const NOT_CONTROL = new Set(["hidden", "submit", "button", "reset", "image"]);
/** Attributes and inputs that name a field. */
const LABEL_NAMES = new Set(["label", "title", "aria-label", "ariaLabel", "legend"]);
/** Attributes and inputs that hold a field's help text. */
const HELP_NAMES = new Set(["helpText", "help", "hint", "subtitle", "description", "tooltip", "ngbTooltip", "matTooltip", "helpTextWarning", "helpLink"]);
/** Angular's form bindings: a field whatever the element is. */
const NG_BINDINGS = ["formControlName", "formField", "formControl", "ngModel"];
/** What a native control shows: a wrapper passes one of its own inputs or props into one of these. */
const VALUE_NAMES = ["value", "checked", "defaultValue", "defaultChecked", "ngModel", "formControl", "formField"];
/** Inputs and props that hold a field's choices. */
const CHOICE_NAMES = new Set(["options", "values", "items", "choices"]);

const isLabelTag = (tag: string) => /label$/i.test(tag);
const isHelpTag = (tag: string) => /(help|helptext|hint)$/i.test(tag);
const add = (list: string[], v: string) => { v = v.replace(/\s+/g, " ").trim(); if (v && !list.includes(v)) list.push(v); };
const known = (v: string | null): v is string => v !== null && !v.includes(RUNTIME) && !v.includes(BASE);

interface Words { keys: string[]; texts: string[] }
const noWords = (): Words => ({ keys: [], texts: [] });
function merge(into: Words, from: Words) { from.keys.forEach((k) => add(into.keys, k)); from.texts.forEach((t) => add(into.texts, t)); }
const empty = (w: Words) => w.keys.length === 0 && w.texts.length === 0;

/** A component (React or Angular) that is a field: the inputs or props it passes into a control. Empty for a value accessor. */
type Wrappers = Map<string, string[]>;
const wrappersOf = new WeakMap<Context, Wrappers>();

// ---------------------------------------------------------------- choices

/** A field's choices from the code giving them: `enum:Name`, `list:a|b|c`, or `runtime:<expression>` (from the server or computed). */
function choicesOf(ctx: Context, e: ts.Expression | undefined, depth = 0): string {
  if (!e) return "";
  while (ts.isParenthesizedExpression(e) || ts.isAsExpression(e) || ts.isSatisfiesExpression(e) || ts.isNonNullExpression(e)) e = e.expression;
  const text = `runtime:${e.getText().replace(/\s+/g, " ").slice(0, 120)}`;
  if (depth > 3) return text;
  const enumOf = (x: ts.Expression): string | null => {
    const s = resolved(ctx, ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(x) ? x.name : x));
    const d = s?.valueDeclaration ?? s?.declarations?.[0];
    if (d && ts.isEnumDeclaration(d)) return d.name.text;
    if (d && ts.isEnumMember(d)) return d.parent.name.text;
    return null;
  };
  if (ts.isArrayLiteralExpression(e)) {
    const items: string[] = [];
    let enumName: string | null = null;
    for (const el of e.elements) {
      let x = el;
      while (ts.isParenthesizedExpression(x) || ts.isAsExpression(x)) x = x.expression;
      if (ts.isPropertyAccessExpression(x) && enumOf(x)) { enumName ??= enumOf(x); continue; }
      if (ts.isObjectLiteralExpression(x)) {
        const named = ["label", "title", "name", "text", "displayName", "value"].map((n) => x.properties.find((p) => ts.isPropertyAssignment(p) && p.name.getText().replace(/['"]/g, "") === n) as ts.PropertyAssignment | undefined).find(Boolean);
        const v = named ? labelValue(ctx, named.initializer) : null;
        if (v) { add(items, v); continue; }
        return text;
      }
      const v = stringValue(x, ctx.checker);
      if (known(v)) add(items, v); else return text;
    }
    if (enumName && items.length === 0) return `enum:${enumName}`;
    return items.length > 0 ? `list:${items.join("|")}` : text;
  }
  if (ts.isCallExpression(e)) {
    const callee = e.expression;
    // Object.values(Enum), Object.keys(Enum)
    if (ts.isPropertyAccessExpression(callee) && callee.expression.getText() === "Object" && e.arguments[0] && enumOf(e.arguments[0])) return `enum:${enumOf(e.arguments[0])}`;
    // list.map(…), list.filter(…), signal(list), input(list), of(list): the list they start from.
    if (ts.isPropertyAccessExpression(callee) && ["map", "filter", "sort", "slice", "concat"].includes(callee.name.text)) return choicesOf(ctx, callee.expression, depth + 1).replace(/^runtime:.*/, text);
    if (e.arguments[0] && /^(signal|input|computed|of|model)$/.test(ts.isPropertyAccessExpression(callee) ? callee.expression.getText() : callee.getText())) return choicesOf(ctx, e.arguments[0], depth + 1).replace(/^runtime:.*/, text);
  }
  if (ts.isIdentifier(e) || ts.isPropertyAccessExpression(e)) {
    const s = resolved(ctx, ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(e) ? e.name : e));
    const d = s?.valueDeclaration;
    if (d && (ts.isVariableDeclaration(d) || ts.isPropertyDeclaration(d)) && d.initializer) {
      const inner = choicesOf(ctx, d.initializer, depth + 1);
      if (!inner.startsWith("runtime:")) return inner;
    }
    if (d && ts.isEnumDeclaration(d)) return `enum:${d.name.text}`;
  }
  // An array of an enum's members, by type.
  const type = ctx.checker.getTypeAtLocation(e);
  const element = ctx.checker.isArrayType(type) ? ctx.checker.getTypeArguments(type as ts.TypeReference)[0] : undefined;
  const sym = element?.aliasSymbol ?? element?.getSymbol();
  const ed = sym?.valueDeclaration ?? sym?.declarations?.[0];
  if (ed && ts.isEnumDeclaration(ed)) return `enum:${ed.name.text}`;
  if (ed && ts.isEnumMember(ed)) return `enum:${ed.parent.name.text}`;
  return text;
}

/** A symbol through its import. */
function resolved(ctx: Context, s: ts.Symbol | undefined): ts.Symbol | undefined {
  if (s && s.flags & ts.SymbolFlags.Alias) { try { return ctx.checker.getAliasedSymbol(s); } catch { /* keep it */ } }
  return s;
}

/** Words or a text key a value gives: `'Name'`, `translate('Name')`, `() => t('name')`. */
function labelValue(ctx: Context, e: ts.Expression): string | null {
  const w = tsWords(ctx, e);
  return w.keys[0] ?? w.texts[0] ?? null;
}

// ---------------------------------------------------------------- React

/** The text keys and words an expression gives: string literals, constants, and keys passed to translate/t. */
function tsWords(ctx: Context, e: ts.Node): Words {
  const w = noWords();
  const visit = (n: ts.Node) => {
    // `x === 'a' ? translate('A') : undefined`: what is shown, not what is compared.
    if (ts.isConditionalExpression(n)) { visit(n.whenTrue); visit(n.whenFalse); return; }
    if (ts.isBinaryExpression(n) && n.operatorToken.kind >= ts.SyntaxKind.LessThanToken && n.operatorToken.kind <= ts.SyntaxKind.ExclamationEqualsEqualsToken) return;
    if (ts.isCallExpression(n)) {
      const callee = ts.isPropertyAccessExpression(n.expression) ? n.expression.name.text : ts.isIdentifier(n.expression) ? n.expression.text : "";
      const first = n.arguments[0];
      if (TEXT_CALLS.has(callee) && first && (ts.isStringLiteral(first) || ts.isNoSubstitutionTemplateLiteral(first))) { add(w.keys, first.text); return; }
    }
    if (ts.isStringLiteral(n) || ts.isNoSubstitutionTemplateLiteral(n)) { add(w.texts, n.text); return; }
    if (ts.isJsxText(n)) { add(w.texts, n.text); return; }
    if ((ts.isIdentifier(n) || ts.isPropertyAccessExpression(n)) && !ts.isPropertyAccessExpression(n.parent)) {
      const v = stringValue(n as ts.Expression, ctx.checker);
      if (known(v)) { add(w.texts, v); return; }
    }
    ts.forEachChild(n, visit);
  };
  visit(e);
  return w;
}

type JsxEl = ts.JsxElement | ts.JsxSelfClosingElement;
const opening = (el: JsxEl) => (ts.isJsxElement(el) ? el.openingElement : el);
const tagOf = (el: JsxEl) => opening(el).tagName.getText();

function jsxAttrs(el: JsxEl): Map<string, ts.JsxAttribute> {
  const m = new Map<string, ts.JsxAttribute>();
  for (const a of opening(el).attributes.properties) if (ts.isJsxAttribute(a)) m.set(a.name.getText(), a);
  return m;
}
const attrExpr = (a: ts.JsxAttribute | undefined): ts.Expression | undefined =>
  !a?.initializer ? undefined : ts.isStringLiteral(a.initializer) ? a.initializer : ts.isJsxExpression(a.initializer) ? a.initializer.expression : undefined;
const attrText = (ctx: Context, a: ts.JsxAttribute | undefined): string => {
  const e = attrExpr(a);
  if (!e) return "";
  const v = stringValue(e, ctx.checker);
  return known(v) ? v : e.getText().replace(/\s+/g, " ").slice(0, 120);
};

/** The component a JSX tag names: its point id, through imports. */
function tagId(ctx: Context, el: JsxEl): string | null {
  const name = opening(el).tagName;
  if (!/^[A-Z]/.test(name.getText().split(".").pop() ?? "")) return null;
  let s = ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(name) ? name.name : name);
  if (s && s.flags & ts.SymbolFlags.Alias) { try { s = ctx.checker.getAliasedSymbol(s); } catch { /* keep it */ } }
  return ctx.symbolId(s);
}

interface ReactComponent { id: string; body: ts.Node; props: Map<string, string>; rest: Set<string>; propsObjects: Set<string>; usesThisProps: boolean }

/** A function or class component: a capitalised function, a const holding one (also through memo/forwardRef), or a class with render. */
function reactComponents(ctx: Context, sf: ts.SourceFile): ReactComponent[] {
  const out: ReactComponent[] = [];
  const fromFunction = (decl: ts.Node, fn: ts.SignatureDeclaration & { body?: ts.ConciseBody }) => {
    const id = ctx.declId(decl);
    if (!id || !fn.body) return;
    const c: ReactComponent = { id, body: fn.body, props: new Map(), rest: new Set(), propsObjects: new Set(), usesThisProps: false };
    const p = fn.parameters[0]?.name;
    if (p && ts.isObjectBindingPattern(p)) bindings(p, c);
    else if (p && ts.isIdentifier(p)) c.propsObjects.add(p.text);
    out.push(c);
  };
  const visit = (n: ts.Node) => {
    if (ts.isFunctionDeclaration(n) && n.name && /^[A-Z]/.test(n.name.text)) fromFunction(n, n);
    else if (ts.isVariableDeclaration(n) && ts.isIdentifier(n.name) && /^[A-Z]/.test(n.name.text) && n.initializer) {
      let init: ts.Expression = n.initializer;
      while (ts.isCallExpression(init) && init.arguments[0]) init = init.arguments[0]; // memo(forwardRef(…))
      if (ts.isArrowFunction(init) || ts.isFunctionExpression(init)) fromFunction(n, init);
    } else if (ts.isClassDeclaration(n) && n.name && n.members.some((m) => ts.isMethodDeclaration(m) && m.name.getText() === "render")) {
      const id = ctx.declId(n);
      if (id) out.push({ id, body: n, props: new Map(), rest: new Set(), propsObjects: new Set(), usesThisProps: true });
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
  // Destructuring of the props object inside the body: `const { value, ...rest } = props` (or `this.props`).
  for (const c of out) {
    const walk = (n: ts.Node) => {
      if (ts.isVariableDeclaration(n) && ts.isObjectBindingPattern(n.name) && n.initializer
          && ((ts.isIdentifier(n.initializer) && c.propsObjects.has(n.initializer.text)) || n.initializer.getText() === "this.props")) bindings(n.name, c);
      ts.forEachChild(n, walk);
    };
    walk(c.body);
  }
  return out;
}

function bindings(p: ts.ObjectBindingPattern, c: ReactComponent) {
  for (const b of p.elements) {
    if (!ts.isIdentifier(b.name)) continue;
    if (b.dotDotDotToken) c.rest.add(b.name.text);
    else c.props.set(b.name.text, b.propertyName ? b.propertyName.getText() : b.name.text);
  }
}

/** The prop an expression is, as given: `value`, `props.value`, `this.props.value` (not `settings.value`, not a computation). */
function propIs(c: ReactComponent, e: ts.Expression | undefined): string | null {
  if (!e) return null;
  while (ts.isParenthesizedExpression(e) || ts.isNonNullExpression(e) || ts.isAsExpression(e)) e = e.expression;
  if (ts.isIdentifier(e)) return c.props.get(e.text) ?? null;
  if (ts.isPropertyAccessExpression(e) && ((ts.isIdentifier(e.expression) && c.propsObjects.has(e.expression.text)) || e.expression.getText() === "this.props")) return e.name.text;
  return null;
}

/** Spreads that pass the component's own props on: `{...props}`, `{...rest}`, `{...this.props}`. */
function spreadsProps(c: ReactComponent, el: JsxEl): boolean {
  return opening(el).attributes.properties.some((a) => ts.isJsxSpreadAttribute(a)
    && ((ts.isIdentifier(a.expression) && (c.rest.has(a.expression.text) || c.propsObjects.has(a.expression.text))) || a.expression.getText() === "this.props"));
}

function jsxElements(n: ts.Node, visit: (el: JsxEl) => void) {
  const walk = (x: ts.Node) => {
    if (ts.isJsxElement(x) || ts.isJsxSelfClosingElement(x)) visit(x);
    ts.forEachChild(x, walk);
  };
  walk(n);
}

// ---------------------------------------------------------------- Angular

type Obj = Record<string, unknown>;
const SPANS = new Set(["sourceSpan", "startSourceSpan", "endSourceSpan", "keySpan", "valueSpan", "nameSpan", "handlerSpan", "i18n"]);
const NOT_MARKUP = new Set(["attributes", "inputs", "outputs", "references", "variables", "templateAttrs", "expression", "value", "handler",
  "item", "contextVariables", "trackBy", "parameters", "triggers", "prefetchTriggers", "hydrateTriggers", "expressionAlias"]);
const ctorName = (o: unknown) => ((o as { constructor?: { name?: string } } | null)?.constructor?.name ?? "");
const isNgElement = (o: Obj) => typeof o.name === "string" && Array.isArray(o.attributes) && Array.isArray(o.children) && Array.isArray(o.inputs);
const astOf = (v: unknown): Obj | null => { const o = v as Obj | null; return o && typeof o === "object" ? ((o.ast as Obj | undefined) ?? o) : null; };
const sourceOf = (v: unknown): string => { const o = v as { source?: unknown } | null; return typeof o?.source === "string" ? o.source.trim() : ""; };

function ngCollect(node: unknown, visit: (n: Obj) => boolean | void, seen = new Set<object>()) {
  if (!node || typeof node !== "object" || seen.has(node as object)) return;
  seen.add(node as object);
  if (Array.isArray(node)) { for (const x of node) ngCollect(x, visit, seen); return; }
  const o = node as Obj;
  if (visit(o) === false) return;
  for (const [k, v] of Object.entries(o)) if (!SPANS.has(k)) ngCollect(v, visit, seen);
}

/** Keys and words in a template expression or markup: `t('x')` (under the prefix), `'x' | translate`, literals, text. */
function ngWords(node: unknown, prefix: string): Words {
  const w = noWords();
  ngCollect(node, (n) => {
    const ctor = ctorName(n);
    if (ctor === "Text" && typeof n.value === "string") add(w.texts, n.value);
    const recv = n.receiver as Obj | undefined;
    const args = n.args as Obj[] | undefined;
    if (/^Call|SafeCall/.test(ctor) && recv && typeof recv.name === "string" && TEXT_CALLS.has(recv.name) && args?.[0] && ctorName(args[0]) === "LiteralPrimitive" && typeof args[0].value === "string") {
      add(w.keys, scoped(recv.name, args[0].value, prefix));
      return false;
    }
    if (ctor === "BindingPipe" && (n.name === "translate" || n.name === "transloco") && ctorName(n.exp) === "LiteralPrimitive" && typeof (n.exp as Obj).value === "string") {
      add(w.keys, (n.exp as { value: string }).value);
      return false;
    }
    if (ctor === "LiteralPrimitive" && typeof n.value === "string") add(w.texts, n.value);
  });
  return w;
}

/** An element's attribute or input by name: its words, and its value as written. */
function ngAttr(o: Obj, names: Set<string> | string[], prefix: string): { name: string; words: Words; source: string } | null {
  const has = (n: string) => (Array.isArray(names) ? names.includes(n) : names.has(n));
  for (const a of o.attributes as Obj[]) if (typeof a.name === "string" && has(a.name) && typeof a.value === "string" && a.value.trim())
    return { name: a.name, words: { keys: [], texts: [a.value.trim()] }, source: a.value.trim() };
  for (const a of o.inputs as Obj[]) if (typeof a.name === "string" && has(a.name.replace(/^attr\./, "")))
    return { name: a.name.replace(/^attr\./, ""), words: ngWords(astOf(a.value), prefix), source: sourceOf(a.value) };
  return null;
}

/**
 * The members of an Angular component bound two ways, Angular's own convention for a value given and handed back: a `model()`,
 * or an `input()`/`@Input()` named X beside an `output()`/`@Output()` named `XChange`. A one-way input (a card's `selected`) isn't one.
 */
function ngInputs(cls: ts.ClassDeclaration): Set<string> {
  const inputs = new Set<string>(), models = new Set<string>(), outputs = new Set<string>();
  for (const m of cls.members) {
    if (!ts.isPropertyDeclaration(m) && !ts.isSetAccessorDeclaration(m)) continue;
    const name = m.name.getText();
    const init = ts.isPropertyDeclaration(m) ? m.initializer : undefined;
    const callee = init && ts.isCallExpression(init) ? init.expression.getText() : "";
    const decorators = (ts.getDecorators(m) ?? []).map((d) => d.expression.getText());
    if (/^model(\.required)?$/.test(callee)) models.add(name);
    else if (/^input(\.required)?$/.test(callee) || decorators.some((d) => /^Input\b/.test(d))) inputs.add(name);
    if (/^output$/.test(callee) || decorators.some((d) => /^Output\b/.test(d)) || /EventEmitter/.test(init?.getText() ?? "")) outputs.add(name);
  }
  for (const i of inputs) if (outputs.has(`${i}Change`)) models.add(i);
  return models;
}

/** The component input an expression is, as given: `value()`, `value`, `this.value`. */
function ngIs(ast: Obj | null, inputs: Set<string>): string | null {
  if (!ast) return null;
  if (/^Call|SafeCall/.test(ctorName(ast)) && Array.isArray(ast.args) && ast.args.length === 0) ast = ast.receiver as Obj;
  return ast && /PropertyRead/.test(ctorName(ast)) && typeof ast.name === "string" && inputs.has(ast.name) && /ImplicitReceiver|ThisReceiver/.test(ctorName(ast.receiver)) ? ast.name : null;
}

interface NgNode { o: Obj; tag: string; parent: NgNode | null; kids: NgNode[] }

/** The template's elements as a tree; `@if`, `@for` and `<ng-template>` wrappers are see-through. */
function ngTree(nodes: unknown): NgNode {
  const root: NgNode = { o: {}, tag: "", parent: null, kids: [] };
  const seen = new Set<object>();
  const visit = (node: unknown, parent: NgNode) => {
    if (!node || typeof node !== "object" || seen.has(node as object)) return;
    seen.add(node as object);
    if (Array.isArray(node)) { for (const x of node) visit(x, parent); return; }
    const o = node as Obj;
    let here = parent;
    if (isNgElement(o)) { here = { o, tag: o.name as string, parent, kids: [] }; parent.kids.push(here); }
    for (const [k, v] of Object.entries(o)) if (!SPANS.has(k) && !NOT_MARKUP.has(k) && v && typeof v === "object") visit(v, here);
  };
  visit(nodes, root);
  return root;
}

function* ngAll(n: NgNode): Generator<NgNode> { for (const k of n.kids) { yield k; yield* ngAll(k); } }

// ---------------------------------------------------------------- wrappers (first pass)

/**
 * D329's rule for wrapper components: a component whose own template binds a control's value to something it receives — a
 * native `<input>`/`<select>`/`<textarea>` whose `value`/`checked`/`ngModel`/`formControl`/`formField` reads one of its own
 * inputs or props, or another wrapper given one of them (by name or a spread of its props). An Angular value accessor
 * (`writeValue` and `registerOnChange`) is one too. Never by the component's name.
 */
export function collectWrappers(ctx: Context, files: ts.SourceFile[]) {
  const wrappers: Wrappers = new Map();
  wrappersOf.set(ctx, wrappers);
  const react = files.flatMap((sf) => (/\.(tsx|jsx|js)$/.test(sf.fileName) ? reactComponents(ctx, sf) : []));
  const ng = [...ctx.angular.components.values()].filter((c) => files.includes(c.cls.getSourceFile()));
  const ngParsed = new Map<string, { tree: NgNode; inputs: Set<string> }>();
  for (const c of ng) {
    if (c.cls.members.some((m) => m.name?.getText() === "writeValue") && c.cls.members.some((m) => m.name?.getText() === "registerOnChange")) wrappers.set(c.id, []);
    if (!c.template) continue;
    try { ngParsed.set(c.id, { tree: ngTree(parseTemplate(c.template.text, c.template.file, { preserveWhitespaces: false }).nodes), inputs: ngInputs(c.cls) }); } catch { /* unreadable: angularTemplates reports it */ }
  }
  const grow = (id: string, props: string[]) => {
    const list = wrappers.get(id) ?? [];
    let changed = !wrappers.has(id);
    for (const p of props) if (!list.includes(p)) { list.push(p); changed = true; }
    wrappers.set(id, list);
    return changed;
  };
  for (let changed = true, rounds = 0; changed && rounds < 10; rounds++) {
    changed = false;
    // Every control the component renders must take one of its props as given; a page passing a prop into one field among
    // others isn't a wrapper.
    for (const c of react) {
      const found: string[] = [];
      let controls = 0, passing = 0;
      jsxElements(c.body, (el) => {
        const tag = tagOf(el);
        const attrs = jsxAttrs(el);
        const inner = NATIVE.has(tag) ? null : tagId(ctx, el);
        const names = NATIVE.has(tag) ? VALUE_NAMES : inner && inner !== c.id ? wrappers.get(inner) : undefined;
        if (!names) return;
        if (NATIVE.has(tag) && NOT_CONTROL.has(attrText(ctx, attrs.get("type")).toLowerCase())) return;
        controls++;
        const before = found.length;
        // Two-way by React's convention: the prop keeps the control's own name (`value={value}`), or reaches it through a spread.
        for (const v of names) { const p = propIs(c, attrExpr(attrs.get(v))); if (p && p === v) add(found, p); }
        if (!NATIVE.has(tag) && spreadsProps(c, el)) names.forEach((p) => add(found, p));
        if (found.length > before || names.some((v) => propIs(c, attrExpr(attrs.get(v))) === v)) passing++;
      });
      if (controls > 0 && passing === controls && found.length > 0 && grow(c.id, found)) changed = true;
    }
    for (const c of ng) {
      const parsed = ngParsed.get(c.id);
      if (!parsed || parsed.inputs.size === 0) continue;
      const found: string[] = [];
      let controls = 0, passing = 0;
      for (const n of ngAll(parsed.tree)) {
        const inner = NATIVE.has(n.tag) ? null : (ctx.angular.selectors.get(n.tag) ?? []).find((id) => wrappers.has(id) && id !== c.id);
        if (!NATIVE.has(n.tag) && !inner) continue;
        if (NATIVE.has(n.tag) && NOT_CONTROL.has((ngAttr(n.o, ["type"], "")?.source ?? "").toLowerCase())) continue;
        controls++;
        const names = NATIVE.has(n.tag) ? VALUE_NAMES : wrappers.get(inner!)!;
        let passes = false;
        for (const a of n.o.inputs as Obj[]) if (typeof a.name === "string" && names.includes(a.name)) {
          const p = ngIs(astOf(a.value), parsed.inputs);
          if (p) { add(found, p); passes = true; }
        }
        if (passes) passing++;
      }
      if (controls > 0 && passing === controls && found.length > 0 && grow(c.id, found)) changed = true;
    }
  }
}

// ---------------------------------------------------------------- facts (second pass)

interface Field {
  element: string; bind: string; to: string; type: string; via: string; id: string;
  label: Words; help: Words; choices: string; inside: string;
}

function emit(ctx: Context, owner: string, file: string, line: number, f: Field) {
  ctx.out.fact("ui", "form-field", owner, file, line, {
    element: f.element, bind: f.bind, to: f.to, type: f.type, via: f.via, id: f.id,
    label: f.label.keys.join("|"), labelText: f.label.texts.join(" "), help: f.help.keys.join("|"), helpText: f.help.texts.join(" "),
    choices: f.choices, inside: f.inside,
  });
}

export function fieldFacts(ctx: Context, sf: ts.SourceFile) {
  const wrappers = wrappersOf.get(ctx) ?? new Map();
  if (/\.(tsx|jsx|js)$/.test(sf.fileName)) reactFields(ctx, sf, wrappers);
  angularFields(ctx, sf, wrappers);
}

function reactFields(ctx: Context, sf: ts.SourceFile, wrappers: Wrappers) {
  const components = reactComponents(ctx, sf);
  const insideOf = (n: ts.Node) => {
    const c = components.filter((x) => x.body.pos <= n.pos && n.end <= x.body.end).sort((a, b) => (a.body.end - a.body.pos) - (b.body.end - b.body.pos))[0];
    return c && wrappers.has(c.id) ? "wrapper" : "";
  };
  const fields = new Map<JsxEl, Omit<Field, "label" | "help">>();
  jsxElements(sf, (el) => {
    const tag = tagOf(el);
    const attrs = jsxAttrs(el);
    const type = attrText(ctx, attrs.get("type")) || (NATIVE.has(tag) ? (tag === "input" ? "text" : tag) : "");
    if (NATIVE.has(tag) && NOT_CONTROL.has(type.toLowerCase())) return;
    const wrapper = NATIVE.has(tag) ? undefined : wrappers.get(tagId(ctx, el) ?? "");
    let bind = "", via = "";
    if (wrapper) { via = "wrapper"; bind = wrapper.find((p) => attrs.has(p)) ?? (attrs.has("name") ? "name" : ""); }
    else if (attrs.has("name") && attrs.has("onChange")) { via = "name+onChange"; bind = "name"; }
    else if (NATIVE.has(tag)) { via = "native"; bind = VALUE_NAMES.find((p) => attrs.has(p)) ?? (attrs.has("name") ? "name" : ""); }
    else return;
    let choices = "";
    const choiceAttr = [...attrs.keys()].find((k) => CHOICE_NAMES.has(k));
    if (choiceAttr) choices = choicesOf(ctx, attrExpr(attrs.get(choiceAttr)));
    else if (tag === "select" && ts.isJsxElement(el)) {
      const items: string[] = [];
      jsxElements(el, (o) => { if (tagOf(o) === "option") { const v = attrText(ctx, jsxAttrs(o).get("value")); const w = childWords(ctx, o); add(items, w.keys[0] ?? w.texts[0] ?? v); } });
      if (items.length > 0) choices = `list:${items.join("|")}`;
    }
    fields.set(el, { element: tag, bind, to: bind ? attrText(ctx, attrs.get(bind)) : "", type, via, id: attrText(ctx, attrs.get("id")), choices, inside: insideOf(el) });
  });
  if (fields.size === 0) return;

  // Labels: the field's own label attribute; a label element pointing at its id; a label element just before it; an element
  // around it holding no other field, with a label attribute. Help likewise, and every help element beside it.
  const labelsFor = new Map<string, Words>();
  jsxElements(sf, (el) => { if (isLabelTag(tagOf(el))) { const f = attrText(ctx, jsxAttrs(el).get("htmlFor") ?? jsxAttrs(el).get("for")); if (f) labelsFor.set(f, childWords(ctx, el)); } });
  const holdsField = (n: ts.Node): number => { let k = 0; jsxElements(n, (x) => { if (fields.has(x)) k++; }); return k; };
  for (const [el, f] of fields) {
    const attrs = jsxAttrs(el);
    const label = noWords(), help = noWords();
    for (const [k, a] of attrs) { if (LABEL_NAMES.has(k)) merge(label, tsWords(ctx, attrExpr(a) ?? a)); if (HELP_NAMES.has(k)) merge(help, tsWords(ctx, attrExpr(a) ?? a)); }
    if (empty(label) && f.id && labelsFor.has(f.id)) merge(label, labelsFor.get(f.id)!);
    // Up to three elements up: siblings before (label) and around (help), while the element holds this field only.
    let child: ts.Node = el;
    for (let up = 0; up < 3; up++) {
      const parent = jsxParent(child);
      if (!parent) break;
      const siblings = parent.children.filter((c): c is JsxEl => ts.isJsxElement(c) || ts.isJsxSelfClosingElement(c));
      const at = siblings.indexOf(child as JsxEl);
      if (empty(label)) for (let i = at - 1; i >= 0; i--) {
        if (holdsField(siblings[i]) > 0 || fields.has(siblings[i])) break;
        if (isLabelTag(tagOf(siblings[i]))) { merge(label, childWords(ctx, siblings[i])); break; }
      }
      for (const s of siblings) if (s !== child && isHelpTag(tagOf(s))) { const ha = jsxAttrs(s).get("text"); merge(help, ha ? tsWords(ctx, attrExpr(ha) ?? ha) : childWords(ctx, s)); }
      if (holdsField(parent) > 1) break;
      if (empty(label)) for (const [k, a] of jsxAttrs(parent)) if (LABEL_NAMES.has(k)) merge(label, tsWords(ctx, attrExpr(a) ?? a));
      if (empty(help)) for (const [k, a] of jsxAttrs(parent)) if (HELP_NAMES.has(k)) merge(help, tsWords(ctx, attrExpr(a) ?? a));
      if (!empty(label) && !empty(help)) break;
      child = parent;
    }
    emit(ctx, ctx.ownerId(el), ctx.rel(sf.fileName), ctx.line(opening(el)), { ...f, label, help });
  }
}

function jsxParent(n: ts.Node): ts.JsxElement | null {
  for (let p = n.parent; p; p = p.parent) {
    if (ts.isJsxElement(p)) return p;
    if (ts.isJsxSelfClosingElement(p) || ts.isFunctionLike(p)) return null;
  }
  return null;
}

/** What a label element says: its text, keys in it, and constants it shows. */
function childWords(ctx: Context, el: JsxEl): Words {
  if (!ts.isJsxElement(el)) return noWords();
  const w = noWords();
  for (const c of el.children) merge(w, tsWords(ctx, c));
  return w;
}

function angularFields(ctx: Context, sf: ts.SourceFile, wrappers: Wrappers) {
  for (const c of ctx.angular.components.values()) {
    if (c.cls.getSourceFile() !== sf || !c.template) continue;
    const t = c.template;
    const templateId = `ts|${t.file}#${c.id.slice(c.id.indexOf("#") + 1)}.template`;
    let parsed;
    try { parsed = parseTemplate(t.text, t.file, { preserveWhitespaces: false }); } catch { continue; }
    const scopes = translocoScopes(parsed.nodes);
    const tree = ngTree(parsed.nodes);
    const prefixOf = (n: NgNode) => scopes.get(n.o) ?? "";
    const memberInit = (name: string): ts.Expression | undefined => {
      const m = c.cls.members.find((x) => x.name?.getText() === name);
      return m && (ts.isPropertyDeclaration(m) || ts.isGetAccessorDeclaration(m)) ? (ts.isPropertyDeclaration(m) ? m.initializer : undefined) : undefined;
    };
    const fields = new Map<NgNode, Omit<Field, "label" | "help">>();
    for (const n of ngAll(tree)) {
      const o = n.o;
      const typeAttr = ngAttr(o, ["type"], "");
      const type = typeAttr?.source || (NATIVE.has(n.tag) ? (n.tag === "input" ? "text" : n.tag) : "");
      if (NATIVE.has(n.tag) && NOT_CONTROL.has(type.toLowerCase())) continue;
      const wrapperId = NATIVE.has(n.tag) ? undefined : (ctx.angular.selectors.get(n.tag) ?? []).find((id) => wrappers.has(id));
      const binding = ngAttr(o, NG_BINDINGS, "");
      let bind = "", to = "", via = "";
      if (binding) { via = "binding"; bind = binding.name; to = binding.source; }
      else if (wrapperId) {
        via = "wrapper";
        const v = ngAttr(o, wrappers.get(wrapperId)!, "");
        bind = v?.name ?? ""; to = v?.source ?? "";
      } else if (NATIVE.has(n.tag)) {
        via = "native";
        const v = ngAttr(o, ["value", "checked", "name"], "");
        bind = v?.name ?? ""; to = v?.source ?? "";
      } else continue;
      let choices = "";
      const choiceAttr = (o.inputs as Obj[]).find((a) => typeof a.name === "string" && CHOICE_NAMES.has(a.name));
      if (choiceAttr) {
        const ast = astOf(choiceAttr.value);
        const read = ast && /PropertyRead|Call/.test(ctorName(ast)) ? ((ast.name as string | undefined) ?? ((ast.receiver as Obj | undefined)?.name as string | undefined)) : undefined;
        const init = read ? memberInit(read) : undefined;
        choices = init ? choicesOf(ctx, init).replace(/^runtime:.*/, `runtime:${sourceOf(choiceAttr.value)}`) : `runtime:${sourceOf(choiceAttr.value)}`;
        // D333 (fresh check): options drawn through the field's own <ng-template> (`{{ rd | writingStyle }}`) show what the template
        // makes of each value at runtime, not the values themselves.
        if (choices.startsWith("list:") && (o.children as Obj[]).some((x) => /^Template$/.test(ctorName(x)) || (isNgElement(x) && x.name === "ng-template")))
          choices = `runtime:${sourceOf(choiceAttr.value)}`;
      } else if (n.tag === "select") {
        const items: string[] = [];
        let loop = "";
        ngCollect(o.children, (x) => {
          if (/ForLoopBlock/.test(ctorName(x))) loop ||= sourceOf(x.expression);
          if (Array.isArray(x.templateAttrs)) for (const a of x.templateAttrs as Obj[]) if (a.name === "ngForOf") loop ||= sourceOf(a.value);
          if (isNgElement(x) && x.name === "option") {
            const w = ngWords(x.children, prefixOf(n));
            const v = ngAttr(x, ["value", "ngValue"], "");
            const word = w.keys[0] ?? w.texts[0] ?? v?.source ?? "";
            if (word) add(items, word);
          }
        });
        choices = loop ? `runtime:${loop}` : items.length > 0 ? `list:${items.join("|")}` : "";
      }
      const id = ngAttr(o, ["id", "inputId"], "")?.source ?? "";
      fields.set(n, { element: n.tag, bind, to, type, via, id, choices, inside: wrappers.has(c.id) ? "wrapper" : "" });
    }
    if (fields.size === 0) continue;
    const labelsFor = new Map<string, Words>();
    for (const n of ngAll(tree)) if (isLabelTag(n.tag)) { const f = ngAttr(n.o, ["for", "htmlFor"], ""); if (f?.source) labelsFor.set(f.source, ngWords(n.o.children, prefixOf(n))); }
    const holdsField = (n: NgNode): number => { let k = fields.has(n) ? 1 : 0; for (const x of ngAll(n)) if (fields.has(x)) k++; return k; };
    for (const [n, f] of fields) {
      const prefix = prefixOf(n);
      const label = noWords(), help = noWords();
      const own = ngAttr(n.o, LABEL_NAMES, prefix); if (own) merge(label, own.words);
      const ownHelp = ngAttr(n.o, HELP_NAMES, prefix); if (ownHelp) merge(help, ownHelp.words);
      if (empty(label) && f.id && labelsFor.has(f.id)) merge(label, labelsFor.get(f.id)!);
      let child = n;
      for (let up = 0; up < 3 && child.parent && child.parent.parent; up++) {
        const parent = child.parent;
        const at = parent.kids.indexOf(child);
        if (empty(label)) for (let i = at - 1; i >= 0; i--) {
          if (holdsField(parent.kids[i]) > 0) break;
          if (isLabelTag(parent.kids[i].tag)) { merge(label, ngWords(parent.kids[i].o.children, prefixOf(parent.kids[i]))); break; }
        }
        for (const s of parent.kids) if (s !== child && isHelpTag(s.tag)) merge(help, ngAttr(s.o, ["text"], prefixOf(s))?.words ?? ngWords(s.o.children, prefixOf(s)));
        if (holdsField(parent) > 1) break;
        if (empty(label)) { const a = ngAttr(parent.o, LABEL_NAMES, prefixOf(parent)); if (a) merge(label, a.words); }
        if (empty(help)) { const a = ngAttr(parent.o, HELP_NAMES, prefixOf(parent)); if (a) merge(help, a.words); }
        if (!empty(label) && !empty(help)) break;
        child = parent;
      }
      const line = t.line + ((n.o.sourceSpan as { start?: { line?: number } } | undefined)?.start?.line ?? 0);
      emit(ctx, templateId, t.file, line, { ...f, label, help });
    }
  }
}

// ---------------------------------------------------------------- screen titles (D333)

/** Elements whose `title` is a tooltip on the control, never a title over what it holds. */
const NOT_TITLED = new Set(["button", "a", "input", "select", "textarea", "img", "option", "label", "span", "i"]);
const HEADING = /^h([1-6])$/i;

/**
 * D333: what a screen's own markup says its title is, `screen-title` facts on the template (Angular) or component (React):
 * `via` `title-attribute`, the outermost element that wraps other elements and carries a `title` with words (Sonarr's
 * `<PageContent title={translate('Series')}>`); `via` `heading`, the first of the highest-level headings (`h1`, else `h2`, …)
 * with words, `level` its number. `label` keys (under transloco's prefix), `labelText` words. Layer 3 picks one per screen.
 */
export function titleFacts(ctx: Context, sf: ts.SourceFile) {
  if (/\.(tsx|jsx|js)$/.test(sf.fileName))
    for (const c of reactComponents(ctx, sf)) {
      const roots: JsxEl[] = [];
      const top = (x: ts.Node) => {
        if (ts.isJsxElement(x) || ts.isJsxSelfClosingElement(x)) { roots.push(x); return; }
        if (ts.isFunctionLike(x) && x !== c.body.parent) return;
        ts.forEachChild(x, top);
      };
      top(c.body);
      if (roots.length === 0) continue;
      const file = ctx.rel(sf.fileName);
      // Breadth first from the outermost elements: the first wrapping element with a title.
      let level: JsxEl[] = roots;
      let titled: { el: JsxEl; words: Words } | null = null;
      for (let depth = 0; depth < 4 && level.length > 0 && !titled; depth++) {
        for (const el of level) {
          if (!ts.isJsxElement(el) || NOT_TITLED.has(tagOf(el).toLowerCase())) continue;
          const a = jsxAttrs(el).get("title");
          const w = a ? tsWords(ctx, attrExpr(a) ?? a) : noWords();
          if (!empty(w)) { titled = { el, words: w }; break; }
        }
        level = level.flatMap((el) => (ts.isJsxElement(el) ? el.children.flatMap(jsxChildren) : []));
      }
      if (titled) ctx.out.fact("ui", "screen-title", c.id, file, ctx.line(opening(titled.el)), titleData("title-attribute", titled.words));
      let best: { el: JsxEl; n: number; words: Words } | null = null;
      for (const r of roots) jsxElements(r, (el) => {
        const m = HEADING.exec(tagOf(el));
        if (!m || (best && best.n <= Number(m[1]))) return;
        const w = childWords(ctx, el);
        if (!empty(w)) best = { el, n: Number(m[1]), words: w };
      });
      const b = best as { el: JsxEl; n: number; words: Words } | null;
      if (b) ctx.out.fact("ui", "screen-title", c.id, file, ctx.line(opening(b.el)), { ...titleData("heading", b.words), level: String(b.n) });
    }
  for (const c of ctx.angular.components.values()) {
    if (c.cls.getSourceFile() !== sf || !c.template) continue;
    const t = c.template;
    const templateId = `ts|${t.file}#${c.id.slice(c.id.indexOf("#") + 1)}.template`;
    let parsed;
    try { parsed = parseTemplate(t.text, t.file, { preserveWhitespaces: false }); } catch { continue; }
    const scopes = translocoScopes(parsed.nodes);
    const prefixOf = (n: NgNode) => scopes.get(n.o) ?? "";
    const tree = ngTree(parsed.nodes);
    const lineOf = (n: NgNode) => {
      const span = n.o.sourceSpan as { start?: { line?: number } } | undefined;
      return t.line + (span?.start?.line ?? 0);
    };
    let level = tree.kids;
    let titled: { n: NgNode; words: Words } | null = null;
    for (let depth = 0; depth < 4 && level.length > 0 && !titled; depth++) {
      for (const n of level) {
        if (n.kids.length === 0 || NOT_TITLED.has(n.tag.toLowerCase())) continue;
        const a = ngAttr(n.o, ["title"], prefixOf(n));
        if (a && !empty(a.words)) { titled = { n, words: a.words }; break; }
      }
      level = level.flatMap((n) => n.kids);
    }
    if (titled) ctx.out.fact("ui", "screen-title", templateId, t.file, lineOf(titled.n), titleData("title-attribute", titled.words));
    let best: { n: NgNode; level: number; words: Words } | null = null;
    for (const n of ngAll(tree)) {
      const m = HEADING.exec(n.tag);
      if (!m || (best && best.level <= Number(m[1]))) continue;
      const w = ngWords(n.o.children, prefixOf(n));
      if (!empty(w)) best = { n, level: Number(m[1]), words: w };
    }
    if (best) ctx.out.fact("ui", "screen-title", templateId, t.file, lineOf(best.n), { ...titleData("heading", best.words), level: String(best.level) });
  }
}

const jsxChildren = (c: ts.JsxChild): JsxEl[] => {
  if (ts.isJsxElement(c) || ts.isJsxSelfClosingElement(c)) return [c];
  if (ts.isJsxFragment(c)) return c.children.flatMap(jsxChildren);
  const found: JsxEl[] = [];
  const walk = (x: ts.Node) => { if (ts.isJsxElement(x) || ts.isJsxSelfClosingElement(x)) { found.push(x); return; } ts.forEachChild(x, walk); };
  if (ts.isJsxExpression(c)) walk(c);
  return found;
};

const titleData = (via: string, w: Words) => ({ via, label: w.keys.join("|"), labelText: w.texts.join(" ") });

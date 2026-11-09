import * as fs from "node:fs";
import * as path from "node:path";
import ts from "typescript";
import { Output, type InlineScript, type Options } from "./model.ts";
import { angularTemplates, collectAngular, type AngularIndex } from "./angular.ts";
import { constantValues, domEvents, reactEvents, signalrListeners } from "./ui.ts";
import { httpFacts } from "./http.ts";
import { pages } from "./routes.ts";
import { rootScreens } from "./rootScreens.ts";
import { conditionFacts, effectName, onLoadFacts } from "./flow.ts";

/**
 * The compiler's part of the frontend map (D31): points for declarations, ✅ lines for what the
 * TypeScript checker resolves, tests as points. Angular templates, pages and HTTP calls are added
 * by the feature modules, which share this context.
 */
export interface Context {
  o: Options;
  out: Output;
  program: ts.Program;
  checker: ts.TypeChecker;
  rel(fileName: string): string;
  isOwn(sf: ts.SourceFile): boolean;
  /** The point id for a declaration, or null when it isn't a point (locals, parameters). */
  declId(decl: ts.Node): string | null;
  /** The point id a symbol refers to, following imports; null for library symbols. */
  symbolId(symbol: ts.Symbol | undefined): string | null;
  /** The point that owns code at this node: enclosing test, member, function or file. */
  ownerId(node: ts.Node): string;
  line(node: ts.Node): number;
  angular: AngularIndex;
  /** True when the project's packages are installed, so library types resolve. */
  typed: boolean;
}

export function extract(o: Options): Output {
  const out = new Output();
  const root = path.resolve(o.root);
  // N5: plain browser scripts (no tsconfig), and views' inline scripts as files of their own that belong to the view.
  const scripts = o.files !== undefined;
  const inline = new Map<string, InlineScript>();
  (o.inline ?? []).forEach((s, i) => inline.set(path.join(root, `${s.file}.script${i + 1}.js`), s));
  let program: ts.Program;
  if (scripts) {
    const options: ts.CompilerOptions = { allowJs: true, checkJs: false, noEmit: true, skipLibCheck: true, target: ts.ScriptTarget.ES2020, lib: ["lib.es2020.d.ts", "lib.dom.d.ts"] };
    const host = ts.createCompilerHost(options);
    const { getSourceFile, fileExists, readFile } = host;
    host.getSourceFile = (f, v, onError, create) => (inline.has(path.resolve(f)) ? ts.createSourceFile(f, inline.get(path.resolve(f))!.text, v, true, ts.ScriptKind.JS) : getSourceFile.call(host, f, v, onError, create));
    host.fileExists = (f) => inline.has(path.resolve(f)) || fileExists.call(host, f);
    host.readFile = (f) => (inline.has(path.resolve(f)) ? inline.get(path.resolve(f))!.text : readFile.call(host, f));
    program = ts.createProgram({ rootNames: [...o.files!, ...inline.keys()], options, host });
  } else {
    // A plain React project has only a package.json (D127): its settings are the ones its toolchain assumes.
    const plain = path.basename(o.tsconfig) === "package.json";
    const tsconfig = plain ? o.tsconfig : resolveProjectConfig(o.tsconfig, out);
    const cfg = plain ? { config: plainReactConfig() } : ts.readConfigFile(tsconfig, ts.sys.readFile);
    if ("error" in cfg && cfg.error) out.warning(`tsconfig: ${ts.flattenDiagnosticMessageText(cfg.error.messageText, " ")}`);
    const json = cfg.config ?? {};
    // .js/.jsx files are part of the map even when the project doesn't type-check them.
    json.compilerOptions = { ...(json.compilerOptions ?? {}), allowJs: true, noEmit: true };
    const parsed = ts.parseJsonConfigFileContent(json, ts.sys, path.dirname(tsconfig), undefined, tsconfig);
    program = ts.createProgram({
      rootNames: parsed.fileNames,
      options: { ...parsed.options, noEmit: true, allowJs: true, skipLibCheck: true, checkJs: false },
    });
  }
  const checker = program.getTypeChecker();
  const projectDir = scripts ? path.resolve(o.tsconfig) : path.dirname(path.resolve(o.tsconfig));
  const typed = fs.existsSync(path.join(projectDir, "node_modules")) || fs.existsSync(path.join(root, "node_modules"));
  if (!typed && !scripts) out.warning("node_modules is missing: library types don't resolve, so calls are matched by name (🟡)");

  const ownRel = (f: string) => path.relative(root, path.resolve(f)).split(path.sep).join("/");
  // An inline script's file is its view; its code belongs to the view, and its lines are the view's.
  const rel = (f: string) => inline.get(path.resolve(f))?.file ?? ownRel(f);
  const isInline = (sf: ts.SourceFile) => inline.has(path.resolve(sf.fileName));
  const isOwn = (sf: ts.SourceFile) => {
    const f = path.resolve(sf.fileName);
    return f.startsWith(root + path.sep) && !f.includes(`${path.sep}node_modules${path.sep}`) && !sf.isDeclarationFile && !isInline(sf);
  };
  const files = program.getSourceFiles().filter(isOwn).sort((a, b) => cmp(rel(a.fileName), rel(b.fileName)));
  const inlineFiles = program.getSourceFiles().filter(isInline).sort((a, b) => cmp(ownRel(a.fileName), ownRel(b.fileName)));

  const ctx: Context = {
    o, out, program, checker, rel, isOwn, typed,
    declId: (d) => declId(d, rel),
    symbolId: (s) => symbolId(s, checker, isOwn, rel),
    ownerId: (n) => inline.get(path.resolve(n.getSourceFile().fileName))?.owner ?? ownerId(n, rel),
    line: (n) => n.getSourceFile().getLineAndCharacterOfPosition(n.getStart()).line + (inline.get(path.resolve(n.getSourceFile().fileName))?.line ?? 1),
    angular: { components: new Map(), selectors: new Map(), pipes: new Map() },
  };

  out.node({ id: o.projectId, kind: "project", name: projectName(projectDir), project: o.projectId, file: null, line: 0, endLine: 0, isTest: false });
  for (const sf of files) declare(ctx, sf);
  for (const sf of files) collectAngular(ctx, sf);
  for (const sf of [...files, ...inlineFiles]) references(ctx, sf);
  for (const sf of files) angularTemplates(ctx, sf);
  for (const sf of files) reactEvents(ctx, sf);
  for (const sf of files) { signalrListeners(ctx, sf); constantValues(ctx, sf); }
  for (const sf of files) pages(ctx, sf);
  if (o.rootScreens) rootScreens(ctx, files);
  for (const sf of [...files, ...inlineFiles]) httpFacts(ctx, sf);
  for (const sf of files) onLoadFacts(ctx, sf); // D171, D176
  for (const sf of [...files, ...inlineFiles]) conditionFacts(ctx, sf); // D174
  if (scripts) for (const sf of [...files, ...inlineFiles]) domEvents(ctx, sf);
  if (o.enums) for (const sf of files) enums(ctx, sf);
  return out;
}

/** What a tsconfig would say for a React app written in plain .js. Generated output and packages stay out. */
function plainReactConfig() {
  return {
    include: ["**/*"],
    exclude: ["node_modules", "build", "dist", "coverage", "public"],
    compilerOptions: { jsx: "preserve", target: "esnext", module: "esnext", moduleResolution: "bundler", esModuleInterop: true, allowSyntheticDefaultImports: true },
  };
}

/** A solution-style tsconfig (only "references") points at the app config that has the files. */
function resolveProjectConfig(tsconfig: string, out: Output): string {
  const cfg = ts.readConfigFile(tsconfig, ts.sys.readFile).config;
  const refs: { path: string }[] = cfg?.references ?? [];
  if ((cfg?.files?.length === 0 || (!cfg?.include && !cfg?.files)) && refs.length > 0) {
    const resolved = refs.map((r) => {
      const p = path.resolve(path.dirname(tsconfig), r.path);
      return fs.statSync(p, { throwIfNoEntry: false })?.isDirectory() ? path.join(p, "tsconfig.json") : p;
    });
    const app = resolved.find((r) => /app/i.test(path.basename(r))) ?? resolved[0];
    out.warning(`${path.basename(tsconfig)} only references other configs; using ${path.basename(app)}`);
    return app;
  }
  return tsconfig;
}

function projectName(dir: string): string {
  try {
    const pkg = JSON.parse(fs.readFileSync(path.join(dir, "package.json"), "utf8"));
    if (typeof pkg.name === "string" && pkg.name.length > 0) return pkg.name;
  } catch { /* no package.json beside the tsconfig */ }
  return path.basename(dir);
}

export function cmp(a: string, b: string) { return a < b ? -1 : a > b ? 1 : 0; }

// ---- Points --------------------------------------------------------------------------------

const isFunctionLike = (n: ts.Node | undefined) => !!n && (ts.isArrowFunction(n) || ts.isFunctionExpression(n));

/** The qualified name of a declaration that is a point: Name, Class.member, Interface.member, Alias.member. */
export function qualifiedName(decl: ts.Node): string | null {
  const nameOf = (n: ts.Node) => (n as { name?: ts.Node }).name;
  const text = (n: ts.Node | undefined) => (n && (ts.isIdentifier(n) || ts.isStringLiteral(n) || ts.isPrivateIdentifier(n)) ? n.text : null);
  const parent = decl.parent;
  if (ts.isFunctionDeclaration(decl) || ts.isClassDeclaration(decl) || ts.isInterfaceDeclaration(decl)
      || ts.isTypeAliasDeclaration(decl) || ts.isEnumDeclaration(decl)) {
    if (!ts.isSourceFile(parent) && !ts.isModuleBlock(parent)) return null;
    const hasDefault = decl.modifiers?.some((m) => m.kind === ts.SyntaxKind.DefaultKeyword);
    return text(nameOf(decl)) ?? (hasDefault ? "default" : null);
  }
  if (ts.isVariableDeclaration(decl) && ts.isIdentifier(decl.name)
      && ts.isVariableDeclarationList(parent) && ts.isVariableStatement(parent.parent)
      && (ts.isSourceFile(parent.parent.parent) || ts.isModuleBlock(parent.parent.parent))) {
    return decl.name.text;
  }
  const handler = componentHandler(decl);
  if (handler) return handler;
  const effect = effectName(decl);
  if (effect) return effect;
  if ((ts.isMethodDeclaration(decl) || ts.isPropertyDeclaration(decl) || ts.isGetAccessorDeclaration(decl)
      || ts.isSetAccessorDeclaration(decl) || ts.isConstructorDeclaration(decl)) && ts.isClassLike(parent)) {
    const owner = qualifiedName(parent);
    const name = ts.isConstructorDeclaration(decl) ? "constructor" : text(nameOf(decl));
    return owner && name ? `${owner}.${name}` : null;
  }
  if ((ts.isPropertySignature(decl) || ts.isMethodSignature(decl)) && (ts.isInterfaceDeclaration(parent) || ts.isTypeLiteralNode(parent))) {
    const owner = ts.isInterfaceDeclaration(parent) ? qualifiedName(parent)
      : ts.isTypeAliasDeclaration(parent.parent) ? qualifiedName(parent.parent) : null;
    const name = text(nameOf(decl));
    return owner && name ? `${owner}.${name}` : null;
  }
  if (ts.isEnumMember(decl)) {
    const owner = qualifiedName(parent);
    const name = text(decl.name);
    return owner && name ? `${owner}.${name}` : null;
  }
  return null;
}

/**
 * G5: a function a React component declares in its body (`const handleAddSeriesPress = useCallback(() => …)`,
 * `const onSave = () => …`, `function onClick() {}`) is a point of its own, `Component.handler`, so a button
 * can say which handler it runs. Only directly in a capitalised component's body.
 */
function componentHandler(decl: ts.Node): string | null {
  let name: string | null = null;
  let statement: ts.Node | undefined;
  if (ts.isVariableDeclaration(decl) && ts.isIdentifier(decl.name) && isHandlerInit(decl.initializer)
      && ts.isVariableDeclarationList(decl.parent) && ts.isVariableStatement(decl.parent.parent)) {
    name = decl.name.text;
    statement = decl.parent.parent;
  } else if (ts.isFunctionDeclaration(decl) && decl.name && decl.body) {
    name = decl.name.text;
    statement = decl;
  }
  const block = statement?.parent;
  if (!name || !block || !ts.isBlock(block)) return null;
  const fn = block.parent;
  const owner = ts.isFunctionDeclaration(fn) ? qualifiedName(fn)
    : (ts.isArrowFunction(fn) || ts.isFunctionExpression(fn)) && ts.isVariableDeclaration(fn.parent) ? qualifiedName(fn.parent) : null;
  // D184: a hook's own functions too (`useProviderSettings.saveProvider`), so a value a handler takes from the hook's result
  // can lead to the function it is, not to everything the hook does on render.
  return owner && (/^[A-Z]/.test(owner) || /^use[A-Z0-9]/.test(owner)) && !owner.includes(".") ? `${owner}.${name}` : null;
}

function isHandlerInit(e: ts.Expression | undefined): boolean {
  if (!e) return false;
  if (ts.isArrowFunction(e) || ts.isFunctionExpression(e)) return true;
  if (!ts.isCallExpression(e)) return false;
  const callee = ts.isPropertyAccessExpression(e.expression) ? e.expression.name.text : ts.isIdentifier(e.expression) ? e.expression.text : "";
  const first = e.arguments[0];
  return callee === "useCallback" && !!first && (ts.isArrowFunction(first) || ts.isFunctionExpression(first));
}

function declId(decl: ts.Node, rel: (f: string) => string): string | null {
  const name = qualifiedName(decl);
  return name ? `ts|${rel(decl.getSourceFile().fileName)}#${name}` : null;
}

function symbolId(symbol: ts.Symbol | undefined, checker: ts.TypeChecker, isOwn: (sf: ts.SourceFile) => boolean, rel: (f: string) => string): string | null {
  if (!symbol) return null;
  if (symbol.flags & ts.SymbolFlags.Alias) {
    try { symbol = checker.getAliasedSymbol(symbol); } catch { return null; }
  }
  for (const decl of symbol.declarations ?? []) {
    if (!isOwn(decl.getSourceFile())) continue;
    // `export default X` and `export { X }` point at X's own declaration.
    const id = declId(decl, rel);
    if (id) return id;
    if (ts.isExportAssignment(decl) && ts.isIdentifier(decl.expression)) {
      const target = checker.getSymbolAtLocation(decl.expression);
      if (target && target !== symbol) return symbolId(target, checker, isOwn, rel);
    }
  }
  return null;
}

function kindOf(decl: ts.Node): string {
  if (ts.isClassDeclaration(decl)) return isAngularComponent(decl) ? "component" : "type";
  if (ts.isInterfaceDeclaration(decl) || ts.isTypeAliasDeclaration(decl) || ts.isEnumDeclaration(decl)) return "type";
  if (ts.isFunctionDeclaration(decl) || ts.isMethodDeclaration(decl) || ts.isConstructorDeclaration(decl)
      || ts.isGetAccessorDeclaration(decl) || ts.isSetAccessorDeclaration(decl) || ts.isMethodSignature(decl)) {
    return isReactComponent(decl) ? "component" : "method";
  }
  if (componentHandler(decl) || effectName(decl)) return "method";
  if (ts.isVariableDeclaration(decl)) {
    if (isFunctionLike(decl.initializer) || isWrappedComponent(decl.initializer)) return isReactComponent(decl) ? "component" : "method";
    return "field";
  }
  return "property";
}

export function isAngularComponent(cls: ts.ClassDeclaration): boolean {
  return (ts.getDecorators(cls) ?? []).some((d) => ts.isCallExpression(d.expression)
    && ts.isIdentifier(d.expression.expression) && d.expression.expression.text === "Component");
}

/** memo(...), forwardRef(...) and similar wrappers around a component function. */
function isWrappedComponent(init: ts.Expression | undefined): boolean {
  return !!init && ts.isCallExpression(init) && init.arguments.some(isFunctionLike);
}

/** A React function component: a capitalised function whose body contains JSX. */
export function isReactComponent(decl: ts.Node): boolean {
  const name = qualifiedName(decl);
  if (!name || name.includes(".") || !/^[A-Z]/.test(name)) return false;
  let found = false;
  const visit = (n: ts.Node) => {
    if (found) return;
    if (ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n) || ts.isJsxFragment(n)) { found = true; return; }
    ts.forEachChild(n, visit);
  };
  visit(decl);
  return found;
}

/** Each enum's members and values (C.2), apart from the walked lines. A value the compiler can't work out is null, never a guess. */
function enums(ctx: Context, sf: ts.SourceFile) {
  const file = ctx.rel(sf.fileName);
  const lineOf = (pos: number) => sf.getLineAndCharacterOfPosition(pos).line + 1;
  const visit = (n: ts.Node) => {
    if (ts.isEnumDeclaration(n)) {
      const id = ctx.declId(n);
      if (id) {
        const members = n.members.map((m) => {
          const value = ctx.checker.getConstantValue(m);
          return { name: m.name.getText(sf).replace(/^["']|["']$/g, ""), value: value === undefined ? null : String(value), line: lineOf(m.getStart(sf)) };
        });
        ctx.out.enumDecl({ id, name: n.name.text, file, line: lineOf(n.getStart(sf)), endLine: lineOf(n.getEnd()), members });
      }
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

function declare(ctx: Context, sf: ts.SourceFile) {
  const file = ctx.rel(sf.fileName);
  const fileId = `file:${file}`;
  ctx.out.node({ id: fileId, kind: "file", name: file, project: ctx.o.projectId, file, line: 0, endLine: 0, isTest: false });
  ctx.out.edge(ctx.o.projectId, fileId, "contains");
  const visit = (n: ts.Node) => {
    const id = qualifiedName(n) ? ctx.declId(n) : null;
    if (id) {
      const start = sf.getLineAndCharacterOfPosition(n.getStart(sf)).line + 1;
      const end = sf.getLineAndCharacterOfPosition(n.getEnd()).line + 1;
      const name = id.slice(id.indexOf("#") + 1);
      ctx.out.node({ id, kind: kindOf(n), name, project: ctx.o.projectId, file, line: start, endLine: end, isTest: false });
      const container = ts.isSourceFile(n.parent) || ts.isModuleBlock(n.parent) ? fileId : containerId(ctx, n);
      ctx.out.edge(container, id, "contains");
      // A component renders as a unit: a change to any of its members changes what its page shows.
      if (ts.isClassDeclaration(n.parent) && isAngularComponent(n.parent)) ctx.out.edge(container, id, "uses");
      if (container && (componentHandler(n) || effectName(n))) ctx.out.edge(container, id, "uses"); // same for a React component's handlers and effects
    }
    const test = testName(n);
    if (test) {
      const id2 = `ts|${file}#test:${test}`;
      const start = sf.getLineAndCharacterOfPosition(n.getStart(sf)).line + 1;
      const end = sf.getLineAndCharacterOfPosition(n.getEnd()).line + 1;
      ctx.out.node({ id: id2, kind: "method", name: test, project: ctx.o.projectId, file, line: start, endLine: end, isTest: true });
      ctx.out.edge(fileId, id2, "contains");
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

function containerId(ctx: Context, n: ts.Node): string | null {
  let p: ts.Node | undefined = n.parent;
  while (p && !ts.isSourceFile(p)) {
    if (ts.isTypeLiteralNode(p) && ts.isTypeAliasDeclaration(p.parent)) return ctx.declId(p.parent);
    const id = qualifiedName(p) ? ctx.declId(p) : null;
    if (id) return id;
    p = p.parent;
  }
  return null;
}

const TEST_CALLS = new Set(["it", "test", "fit", "xit"]);
const SUITE_CALLS = new Set(["describe", "fdescribe", "xdescribe", "suite"]);

/** For `it("does x", ...)` inside `describe("Thing", ...)`: "Thing › does x". */
export function testName(n: ts.Node): string | null {
  if (!ts.isCallExpression(n)) return null;
  const callee = n.expression;
  const name = ts.isIdentifier(callee) ? callee.text
    : ts.isPropertyAccessExpression(callee) && ts.isIdentifier(callee.expression) ? callee.expression.text : null;
  if (!name || !TEST_CALLS.has(name)) return null;
  const title = n.arguments[0];
  if (!title || !(ts.isStringLiteral(title) || ts.isNoSubstitutionTemplateLiteral(title))) return null;
  const parts = [title.text];
  for (let p: ts.Node | undefined = n.parent; p; p = p.parent) {
    if (ts.isCallExpression(p) && ts.isIdentifier(p.expression) && SUITE_CALLS.has(p.expression.text)) {
      const t = p.arguments[0];
      if (t && (ts.isStringLiteral(t) || ts.isNoSubstitutionTemplateLiteral(t))) parts.unshift(t.text);
    }
  }
  return parts.join(" › ");
}

function ownerId(n: ts.Node, rel: (f: string) => string): string {
  const sf = n.getSourceFile();
  for (let p: ts.Node | undefined = n; p && !ts.isSourceFile(p); p = p.parent) {
    const test = testName(p);
    if (test) return `ts|${rel(sf.fileName)}#test:${test}`;
    if (p !== n || !ts.isIdentifier(n)) {
      const id = qualifiedName(p) ? declId(p, rel) : null;
      if (id) return id;
    }
  }
  return `file:${rel(sf.fileName)}`;
}

// ---- ✅ lines from the checker --------------------------------------------------------------

function references(ctx: Context, sf: ts.SourceFile) {
  const { checker, out } = ctx;
  contextualCache = new Map();
  const visit = (n: ts.Node) => {
    if (ts.isPropertyAssignment(n) || ts.isShorthandPropertyAssignment(n) || ts.isMethodDeclaration(n) || ts.isJsxAttribute(n) || ts.isBindingElement(n)) {
      const symbol = resolveContextual(checker, n);
      if (symbol) contextualCache.set(n, { symbol });
    }
    if (ts.isIdentifier(n) && !isDeclarationName(n)) {
      const symbol = checker.getSymbolAtLocation(n);
      const target = ctx.symbolId(symbol);
      // T2: a plain script's global declared in several files (admin.common.js and public.common.js) could be either: 🟡 to each.
      const several = target ? declaredIn(ctx, symbol!) : [];
      if (several.length > 1) for (const id of several) out.edge(ctx.ownerId(n), id, edgeKind(n), 1);
      else if (target) out.edge(ctx.ownerId(n), target, edgeKind(n));
      else {
        // `const { addSeries } = useAddSeries()` then `addSeries(…)` in a handler: the handler calls the hook (G5).
        const from = resultOf(ctx, symbol);
        if (from && from !== ctx.ownerId(n)) out.edge(ctx.ownerId(n), from, "calls");
        if (from && from !== ctx.ownerId(n) && edgeKind(n) === "calls") callSite(ctx, sf, n, from, 0);
      }
      if (edgeKind(n) === "calls" || (edgeKind(n) === "renders" && !ts.isJsxClosingElement(n.parent)))
        for (const id of several.length > 1 ? several : target ? [target] : []) callSite(ctx, sf, n, id, several.length > 1 ? 1 : 0);
    } else if (contextualProperty(n)) {
      // `{ roles: [...] }` where an UpdateUserRequest is expected, `<Cell errorMessage={…} />`, and
      // `const { roles } = req`: the name refers to the interface's field through the expected type.
      const target = ctx.symbolId(contextualProperty(n)!.symbol);
      if (target) out.edge(ctx.ownerId(n), target, "uses");
    }
    if (ts.isHeritageClause(n)) {
      for (const t of n.types) {
        const target = ctx.symbolId(checker.getSymbolAtLocation(t.expression));
        const owner = ts.isClassLike(n.parent) || ts.isInterfaceDeclaration(n.parent) ? ctx.declId(n.parent) : null;
        if (owner && target) out.edge(owner, target, n.token === ts.SyntaxKind.ExtendsKeyword ? "inherits" : "implements");
      }
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

/** Every own declaration of a symbol, when they sit in more than one file. */
function declaredIn(ctx: Context, symbol: ts.Symbol): string[] {
  const decls = (symbol.declarations ?? []).filter((d) => ctx.isOwn(d.getSourceFile()));
  if (new Set(decls.map((d) => d.getSourceFile().fileName)).size < 2) return [];
  return [...new Set(decls.map((d) => ctx.declId(d)).filter((id): id is string => !!id))];
}

function contextualProperty(n: ts.Node): { symbol: ts.Symbol | undefined } | null {
  return contextualCache.get(n) ?? null;
}
let contextualCache = new Map<ts.Node, { symbol: ts.Symbol | undefined }>();

/** Resolves property names whose meaning comes from an expected type (object literals, JSX attributes, destructuring). */
function resolveContextual(checker: ts.TypeChecker, n: ts.Node): ts.Symbol | undefined {
  const propOf = (type: ts.Type | undefined, name: string): ts.Symbol | undefined => {
    if (!type) return undefined;
    const direct = type.getProperty(name);
    if (direct) return direct;
    if (type.isUnionOrIntersection()) for (const t of type.types) { const p = t.getProperty(name); if (p) return p; }
    return undefined;
  };
  if ((ts.isPropertyAssignment(n) || ts.isShorthandPropertyAssignment(n) || ts.isMethodDeclaration(n)) && ts.isObjectLiteralExpression(n.parent)
      && n.name && (ts.isIdentifier(n.name) || ts.isStringLiteral(n.name)))
    return propOf(checker.getContextualType(n.parent), n.name.text);
  if (ts.isJsxAttribute(n) && ts.isIdentifier(n.name))
    return propOf(checker.getContextualType(n.parent), n.name.text);
  if (ts.isBindingElement(n) && ts.isObjectBindingPattern(n.parent)) {
    const key = n.propertyName ?? n.name;
    if (ts.isIdentifier(key)) return propOf(checker.getTypeAtLocation(n.parent), key.text);
  }
  return undefined;
}

function isDeclarationName(id: ts.Identifier): boolean {
  const p = id.parent;
  return (p as { name?: ts.Node }).name === id && (ts.isDeclarationStatement(p) || ts.isClassElement(p)
    || ts.isTypeElement(p) || ts.isVariableDeclaration(p) || ts.isParameter(p) || ts.isEnumMember(p)
    || ts.isBindingElement(p) || ts.isPropertyAssignment(p) || ts.isImportSpecifier(p) || ts.isExportSpecifier(p));
}

/** For a local bound from a call's result (`const x = f()`, `const { a } = f()`), the called function's id. */
function resultOf(ctx: Context, symbol: ts.Symbol | undefined): string | null {
  const d = symbol?.declarations?.length === 1 ? symbol.declarations[0] : null;
  if (!d) return null;
  let v: ts.Node = d;
  while (ts.isBindingElement(v) || ts.isObjectBindingPattern(v) || ts.isArrayBindingPattern(v)) v = v.parent;
  if (!ts.isVariableDeclaration(v) || !v.initializer) return null;
  const init = ts.isAwaitExpression(v.initializer) ? v.initializer.expression : v.initializer;
  if (!ts.isCallExpression(init)) return null;
  const callee = ts.isPropertyAccessExpression(init.expression) ? init.expression.name : init.expression;
  const called = ctx.symbolId(ctx.checker.getSymbolAtLocation(callee));
  // D184: `const { saveProvider } = useManageIndexer()`: the function the hook hands back under that name, when its returns say.
  if (d && ts.isBindingElement(d) && ts.isObjectBindingPattern(d.parent) && !d.dotDotDotToken) {
    const prop = d.propertyName && ts.isIdentifier(d.propertyName) ? d.propertyName.text : ts.isIdentifier(d.name) ? d.name.text : null;
    const returned = prop ? returnedPoint(ctx, ctx.checker.getSymbolAtLocation(callee), prop, 0) : null;
    if (returned) return returned;
  }
  return called;
}

/**
 * D184: the point a function returns under a property name: `return { saveProvider }` (a function declared in its body, which is a
 * point of its own), `return { ...useOther() }` or `return useOther()` (followed into that function), up to four calls deep. Null when
 * the returns don't say, so the caller keeps its line to the function itself.
 */
function returnedPoint(ctx: Context, symbol: ts.Symbol | undefined, prop: string, depth: number): string | null {
  if (!symbol || depth > 4) return null;
  if (symbol.flags & ts.SymbolFlags.Alias) {
    try { symbol = ctx.checker.getAliasedSymbol(symbol); } catch { return null; }
  }
  const decl = symbol.declarations?.find((x) => ctx.isOwn(x.getSourceFile()));
  const fn = decl && ts.isVariableDeclaration(decl) ? decl.initializer : decl;
  if (!fn || !(ts.isFunctionDeclaration(fn) || ts.isArrowFunction(fn) || ts.isFunctionExpression(fn)) || !fn.body) return null;
  const results: ts.Expression[] = [];
  if (ts.isBlock(fn.body)) {
    const visit = (n: ts.Node) => {
      if (ts.isReturnStatement(n) && n.expression) results.push(n.expression);
      if (!ts.isFunctionLike(n)) ts.forEachChild(n, visit);
    };
    ts.forEachChild(fn.body, visit);
  } else results.push(fn.body);
  const found = new Set<string>();
  for (const e of results) {
    const r = pointIn(ctx, e, prop, depth);
    if (r) found.add(r);
  }
  return found.size === 1 ? [...found][0] : null;
}

/** The point an expression gives under a property name: a call (followed), an object literal, or a local holding one. */
function pointIn(ctx: Context, e: ts.Expression, prop: string, depth: number): string | null {
  for (let hops = 0; hops < 4; hops++) {
    while (ts.isParenthesizedExpression(e) || ts.isAsExpression(e) || ts.isSatisfiesExpression(e)) e = e.expression;
    if (!ts.isIdentifier(e)) break;
    const d = ctx.checker.getSymbolAtLocation(e)?.declarations?.[0];
    if (!d || !ts.isVariableDeclaration(d) || !d.initializer || d.getSourceFile() !== e.getSourceFile()) return null;
    e = d.initializer;
  }
  if (ts.isCallExpression(e))
    return returnedPoint(ctx, ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(e.expression) ? e.expression.name : e.expression), prop, depth + 1);
  if (!ts.isObjectLiteralExpression(e)) return null;
  for (const p of [...e.properties].reverse()) {
    if ((ts.isShorthandPropertyAssignment(p) || ts.isPropertyAssignment(p)) && ts.isIdentifier(p.name) && p.name.text === prop) {
      const value = ts.isShorthandPropertyAssignment(p) ? ctx.checker.getShorthandAssignmentValueSymbol(p)
        : ts.isIdentifier(p.initializer) ? ctx.checker.getSymbolAtLocation(p.initializer) : undefined;
      const d = value?.declarations?.[0];
      return d ? ctx.declId(d) : null;
    }
    if (ts.isSpreadAssignment(p)) {
      const r = pointIn(ctx, p.expression, prop, depth + 1);
      if (r) return r;
    }
  }
  return null;
}

/**
 * D184: where a call is written, so a flow can quote its line: one `call-site` fact per call, on the calling point, at the line the
 * call expression starts (a chain's first line, `this.a.b().c(`), or a JSX element's tag; `kind` says which. A fact, not a line:
 * impact never reads it.
 */
function callSite(ctx: Context, sf: ts.SourceFile, id: ts.Identifier, target: string, label: 0 | 1) {
  let n: ts.Node = id;
  if (ts.isPropertyAccessExpression(n.parent) && n.parent.name === n) n = n.parent;
  const owner = ctx.ownerId(id);
  if (owner === target) return;
  ctx.out.fact("call-sites", "call-site", owner, ctx.rel(sf.fileName), ctx.line(n.parent), { target, label: String(label), kind: edgeKind(id) });
}

function edgeKind(id: ts.Identifier): string {
  let n: ts.Node = id;
  if (ts.isPropertyAccessExpression(n.parent) && n.parent.name === n) n = n.parent;
  const p = n.parent;
  if (ts.isCallExpression(p) && p.expression === n) return "calls";
  if (ts.isNewExpression(p) && p.expression === n) return "calls";
  if (ts.isTaggedTemplateExpression(p) && p.tag === n) return "calls";
  if ((ts.isJsxOpeningElement(p) || ts.isJsxSelfClosingElement(p) || ts.isJsxClosingElement(p)) && p.tagName === n) return "renders";
  if (ts.isDecorator(p) || (ts.isCallExpression(p) && ts.isDecorator(p.parent))) return "uses";
  if (ts.isExpressionWithTypeArguments(p) && ts.isHeritageClause(p.parent)) return "uses";
  return "uses";
}

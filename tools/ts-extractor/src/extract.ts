import * as fs from "node:fs";
import * as path from "node:path";
import ts from "typescript";
import { Output, type Options } from "./model.ts";
import { angularTemplates, collectAngular, type AngularIndex } from "./angular.ts";
import { httpFacts } from "./http.ts";
import { pages } from "./routes.ts";

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
  const tsconfig = resolveProjectConfig(o.tsconfig, out);
  const cfg = ts.readConfigFile(tsconfig, ts.sys.readFile);
  if (cfg.error) out.warning(`tsconfig: ${ts.flattenDiagnosticMessageText(cfg.error.messageText, " ")}`);
  const json = cfg.config ?? {};
  // .js/.jsx files are part of the map even when the project doesn't type-check them.
  json.compilerOptions = { ...(json.compilerOptions ?? {}), allowJs: true, noEmit: true };
  const parsed = ts.parseJsonConfigFileContent(json, ts.sys, path.dirname(tsconfig), undefined, tsconfig);
  const program = ts.createProgram({
    rootNames: parsed.fileNames,
    options: { ...parsed.options, noEmit: true, allowJs: true, skipLibCheck: true, checkJs: false },
  });
  const checker = program.getTypeChecker();
  const root = path.resolve(o.root);
  const projectDir = path.dirname(path.resolve(o.tsconfig));
  const typed = fs.existsSync(path.join(projectDir, "node_modules")) || fs.existsSync(path.join(root, "node_modules"));
  if (!typed) out.warning("node_modules is missing: library types don't resolve, so calls are matched by name (🟡)");

  const rel = (f: string) => path.relative(root, path.resolve(f)).split(path.sep).join("/");
  const isOwn = (sf: ts.SourceFile) => {
    const f = path.resolve(sf.fileName);
    return f.startsWith(root + path.sep) && !f.includes(`${path.sep}node_modules${path.sep}`) && !sf.isDeclarationFile;
  };
  const files = program.getSourceFiles().filter(isOwn).sort((a, b) => cmp(rel(a.fileName), rel(b.fileName)));

  const ctx: Context = {
    o, out, program, checker, rel, isOwn, typed,
    declId: (d) => declId(d, rel),
    symbolId: (s) => symbolId(s, checker, isOwn, rel),
    ownerId: (n) => ownerId(n, rel),
    line: (n) => n.getSourceFile().getLineAndCharacterOfPosition(n.getStart()).line + 1,
    angular: { components: new Map(), selectors: new Map(), pipes: new Map() },
  };

  out.node({ id: o.projectId, kind: "project", name: projectName(projectDir), project: o.projectId, file: null, line: 0, endLine: 0, isTest: false });
  for (const sf of files) declare(ctx, sf);
  for (const sf of files) collectAngular(ctx, sf);
  for (const sf of files) references(ctx, sf);
  for (const sf of files) angularTemplates(ctx, sf);
  for (const sf of files) pages(ctx, sf);
  for (const sf of files) httpFacts(ctx, sf);
  return out;
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
function isReactComponent(decl: ts.Node): boolean {
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
      const target = ctx.symbolId(checker.getSymbolAtLocation(n));
      if (target) out.edge(ctx.ownerId(n), target, edgeKind(n));
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

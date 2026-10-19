import ts from "typescript";
import type { Context } from "./extract.ts";
import { BASE, RUNTIME, isBaseName, stringValue } from "./values.ts";

const DETECTOR = "frontend-http";
const ANGULAR_VERBS: Record<string, string> = { get: "GET", post: "POST", put: "PUT", delete: "DELETE", patch: "PATCH", head: "HEAD" };

/**
 * Facts the C# `frontend-http` detector links to backend endpoints (D31), plus data shapes for
 * API field links:
 * - `call`: verb + URL template, and the typed result or body when the code names one;
 * - `generated-call`: a call on a generated API client whose file isn't present (NSwag rules apply);
 * - `shape`: an interface's or class's properties, for matching against C# response types.
 * Calls whose URL can't be followed are ❓.
 */
export function httpFacts(ctx: Context, sf: ts.SourceFile) {
  const wrappers = parseWrappers(ctx.o.settings[DETECTOR]?.wrappers ?? []);
  const visit = (n: ts.Node) => {
    if (ts.isCallExpression(n)) call(ctx, n, wrappers);
    if (ts.isInterfaceDeclaration(n) || ts.isClassDeclaration(n) || (ts.isTypeAliasDeclaration(n) && ts.isTypeLiteralNode(n.type))) shape(ctx, n);
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

interface Wrapper { fn: string; urlProp: string | null; verbProp: string | null; verb: string }

/** "useApiQuery:path" (object argument with a `path` property), "useApiMutation:path:method", "api.get" (URL as first argument). */
function parseWrappers(specs: string[]): Wrapper[] {
  return specs.map((s) => {
    const [fn, urlProp = null, verbProp = null] = s.split(":");
    return { fn, urlProp, verbProp, verb: /mutation|post|save/i.test(fn) ? "POST" : "GET" };
  });
}

function call(ctx: Context, n: ts.CallExpression, wrappers: Wrapper[]) {
  const { checker } = ctx;
  const callee = n.expression;
  const name = ts.isIdentifier(callee) ? callee.text : ts.isPropertyAccessExpression(callee) ? callee.name.text : "";
  const receiver = ts.isPropertyAccessExpression(callee) ? callee.expression : null;
  const owner = ctx.ownerId(n);
  const file = ctx.rel(n.getSourceFile().fileName);
  const line = ctx.line(n);

  // In-house wrappers named in stangent.yaml.
  const w = wrappers.find((x) => x.fn === name || x.fn === callee.getText());
  if (w) {
    const arg = n.arguments[0];
    let url: string | null = null;
    let verb = w.verb;
    if (w.urlProp && arg && ts.isObjectLiteralExpression(arg)) {
      url = stringValue(objProp(arg, w.urlProp) ?? undefined, checker);
      const v = w.verbProp ? stringValue(objProp(arg, w.verbProp) ?? undefined, checker) : null;
      if (v) verb = v.toUpperCase();
    } else if (!w.urlProp) {
      url = stringValue(arg, checker);
    }
    // Wrappers add the API root themselves (Sonarr: getQueryPath(path) = urlBase + apiRoot + path),
    // so a relative path is matched against the end of endpoint paths.
    if (url !== null && !/^(https?:|\{base\})/.test(url)) url = BASE + url.replace(/^\//, "");
    emit(ctx, owner, file, line, verb, url, `wrapper:${w.fn}`, typeArg(ctx, n, 0), typeArg(ctx, n, 1));
    return;
  }

  // Angular HttpClient: this.http.get<T>(url, …), post<T>(url, body), request(verb, url).
  if (receiver && (ANGULAR_VERBS[name] || name === "request") && isHttpClient(ctx, receiver)) {
    const [verb, urlArg, bodyArg] = name === "request"
      ? [stringValue(n.arguments[0], checker)?.toUpperCase() ?? "ANY", n.arguments[1], n.arguments[2]]
      : [ANGULAR_VERBS[name], n.arguments[0], n.arguments[1]];
    emit(ctx, owner, file, line, verb, stringValue(urlArg, checker), ctx.typed ? "angular-http" : "angular-http-by-name",
      typeArg(ctx, n, 0), name === "post" || name === "put" || name === "patch" ? valueType(ctx, bodyArg) : null);
    return;
  }

  // fetch(url, { method }) and axios.get/post/…(url) or axios({ url, method }).
  if (ts.isIdentifier(callee) && name === "fetch") {
    const opts = n.arguments[1];
    const verb = opts && ts.isObjectLiteralExpression(opts) ? stringValue(objProp(opts, "method") ?? undefined, checker)?.toUpperCase() ?? "GET" : "GET";
    emit(ctx, owner, file, line, verb, stringValue(n.arguments[0], checker), "fetch", typeArg(ctx, n, 0), null);
    return;
  }
  if (receiver && ts.isIdentifier(receiver) && receiver.text === "axios" && ANGULAR_VERBS[name]) {
    emit(ctx, owner, file, line, ANGULAR_VERBS[name], stringValue(n.arguments[0], checker), "axios", typeArg(ctx, n, 0),
      name === "post" || name === "put" || name === "patch" ? valueType(ctx, n.arguments[1]) : null);
    return;
  }

  // A generated API client whose file isn't in the repo (NSwag writes it at build time).
  if (receiver) {
    const client = generatedClientName(ctx, receiver);
    if (client) ctx.out.fact(DETECTOR, "generated-call", owner, file, line, { client, method: name, generator: "nswag" });
  }
}

function emit(ctx: Context, owner: string, file: string, line: number, verb: string, url: string | null, via: string,
              responseType: string | null, bodyType: string | null) {
  if (url === null || url === RUNTIME || url.replace(BASE, "").replace(/\{\*\}/g, "").replace(/[/?&=]/g, "") === "") {
    ctx.out.blind(owner, file, line, `HTTP ${verb} to a URL built at runtime`, DETECTOR);
    return;
  }
  ctx.out.fact(DETECTOR, "call", owner, file, line, {
    verb, url: url.split("?")[0], via, responseType: responseType ?? "", bodyType: bodyType ?? "",
  });
}

function isHttpClient(ctx: Context, receiver: ts.Expression): boolean {
  const type = ctx.checker.getTypeAtLocation(receiver);
  const symbol = type.getSymbol() ?? type.aliasSymbol;
  if (symbol) {
    const decl = symbol.declarations?.[0];
    const fromAngular = decl ? /@angular[\\/]common[\\/]/.test(decl.getSourceFile().fileName) : false;
    if (symbol.getName() === "HttpClient" && (fromAngular || !ctx.typed)) return true;
  }
  // Without node_modules the type is unknown: fall back to the field's declared type name or its name.
  if (!ctx.typed) {
    const text = receiver.getText();
    return /(^|\.)(http|httpClient|_http)$/i.test(text);
  }
  return false;
}

/** The project's own type behind a type argument: Series in get<Series[]> or get<PaginatedResult<Series[]>>. */
function typeArg(ctx: Context, n: ts.CallExpression, index: number): string | null {
  const t = n.typeArguments?.[index];
  return t ? ownType(ctx, t) : null;
}

function ownType(ctx: Context, t: ts.TypeNode): string | null {
  if (ts.isArrayTypeNode(t)) return ownType(ctx, t.elementType);
  if (ts.isUnionTypeNode(t)) {
    for (const u of t.types) { const id = ownType(ctx, u); if (id) return id; }
    return null;
  }
  if (ts.isTypeReferenceNode(t)) {
    const id = ctx.symbolId(ctx.checker.getSymbolAtLocation(t.typeName));
    if (id) return id;
    // Readonly<T>, Array<T>, Promise<T> and other library wrappers: look inside.
    for (const a of t.typeArguments ?? []) { const inner = ownType(ctx, a); if (inner) return inner; }
  }
  return null;
}

function valueType(ctx: Context, e: ts.Expression | undefined): string | null {
  if (!e) return null;
  const type = ctx.checker.getTypeAtLocation(e);
  const symbol = type.getSymbol() ?? type.aliasSymbol;
  return ctx.symbolId(symbol);
}

function generatedClientName(ctx: Context, receiver: ts.Expression): string | null {
  const symbol = ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(receiver) ? receiver.name : receiver);
  for (const d of symbol?.declarations ?? []) {
    let typeName: string | null = null;
    if ((ts.isVariableDeclaration(d) || ts.isPropertyDeclaration(d) || ts.isParameter(d)) && d.type && ts.isTypeReferenceNode(d.type))
      typeName = d.type.typeName.getText();
    else if ((ts.isVariableDeclaration(d) || ts.isPropertyDeclaration(d)) && d.initializer && ts.isNewExpression(d.initializer))
      typeName = d.initializer.expression.getText();
    if (!typeName || !/Client$/.test(typeName)) continue;
    // Only when the client's own declaration isn't in the repo (otherwise its real calls are followed).
    const typed = d as ts.VariableDeclaration | ts.PropertyDeclaration;
    const typeSymbol = typed.type && ts.isTypeReferenceNode(typed.type) ? ctx.checker.getSymbolAtLocation(typed.type.typeName)
      : typed.initializer && ts.isNewExpression(typed.initializer) ? ctx.checker.getSymbolAtLocation(typed.initializer.expression) : undefined;
    if (!ctx.symbolId(typeSymbol)) return typeName;
  }
  return null;
}

/** An interface's or class's properties with their own types, for API field matching. */
function shape(ctx: Context, n: ts.InterfaceDeclaration | ts.ClassDeclaration | ts.TypeAliasDeclaration) {
  const id = ctx.declId(n);
  if (!id) return;
  const members = ts.isTypeAliasDeclaration(n) ? (n.type as ts.TypeLiteralNode).members : n.members;
  const props: { name: string; id: string; type: string }[] = [];
  for (const m of members) {
    if (!(ts.isPropertySignature(m) || ts.isPropertyDeclaration(m)) || !m.name || !(ts.isIdentifier(m.name) || ts.isStringLiteral(m.name))) continue;
    const memberId = ctx.declId(m);
    if (!memberId) continue;
    props.push({ name: m.name.text, id: memberId, type: m.type ? ownType(ctx, m.type) ?? "" : "" });
  }
  if (props.length > 0)
    ctx.out.fact(DETECTOR, "shape", id, ctx.rel(n.getSourceFile().fileName), ctx.line(n), { props: JSON.stringify(props) });
}

function objProp(o: ts.ObjectLiteralExpression, name: string): ts.Expression | null {
  for (const p of o.properties) {
    if (ts.isPropertyAssignment(p) && (ts.isIdentifier(p.name) || ts.isStringLiteral(p.name)) && p.name.text === name) return p.initializer;
    if (ts.isShorthandPropertyAssignment(p) && p.name.text === name) return p.name;
  }
  return null;
}

export { isBaseName };

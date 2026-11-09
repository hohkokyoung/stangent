import ts from "typescript";
import type { Context } from "./extract.ts";
import { BASE, RUNTIME, isBaseName, stringValue } from "./values.ts";
import { join, paramValues, valuesOf } from "./params.ts";

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

interface Wrapper { fn: string; urlProp: string | null; verbProp: string | null; verb: string; fixed: boolean }

/**
 * "useApiQuery:path" (object argument with a `path` property), "useApiMutation:path:method", "api.get" (URL as first
 * argument). A property written `#N` is the call's Nth argument instead ("send:#1:#0": `send('POST', '/orders')`).
 * `@VERB` gives the verb when the call doesn't ("apiDelete@DELETE"); without it the verb is read off the name
 * (delete/remove → DELETE, put/update → PUT, patch → PATCH, post/save/mutation → POST, else GET).
 */
function parseWrappers(specs: string[]): Wrapper[] {
  return specs.map((s) => {
    const [spec, fixed] = s.split("@");
    const [fn, urlProp = null, verbProp = null] = spec.split(":");
    return { fn, urlProp, verbProp, verb: fixed ? fixed.toUpperCase() : verbFromName(fn), fixed: !!fixed };
  });
}

/** Whole words of the name (camelCase, snake_case, dots): `dispatch` isn't "patch", `fetchUpdates` isn't "update". */
function verbFromName(fn: string): string {
  const words = new Set(fn.replace(/([a-z0-9])([A-Z])/g, "$1 $2").split(/[\s._$-]+/).map((w) => w.toLowerCase()));
  const has = (...ws: string[]) => ws.some((w) => words.has(w));
  if (has("delete", "remove", "del")) return "DELETE";
  if (has("patch")) return "PATCH";
  if (has("put", "update")) return "PUT";
  if (has("mutation", "post", "save")) return "POST";   // not "create": createAjaxRequest names a factory
  return "GET";
}

const prefixes = new Map<ts.Node, string | null>();

/**
 * What a wrapper puts in front of its URL, read from its own body: a call there passes the URL property
 * (`getQueryPath(path)`, `getQueryPath(options.path)`) to a function returning one concatenation that ends
 * with that parameter (`urlBase + apiRoot + path` → `{base}/api/v5`). Null when the code doesn't show it.
 */
function wrapperPrefix(ctx: Context, callee: ts.Expression, urlProp: string): string | null {
  const fn = declaredFunction(ctx.checker, callee);
  if (!fn?.body) return null;
  if (prefixes.has(fn)) return prefixes.get(fn)!;
  let found: string | null = null;
  const visit = (n: ts.Node) => {
    if (found !== null) return;
    if (ts.isCallExpression(n) && n.arguments.length === 1) {
      const a = n.arguments[0];
      const passes = (ts.isIdentifier(a) && a.text === urlProp) || (ts.isPropertyAccessExpression(a) && a.name.text === urlProp);
      const inner = passes ? declaredFunction(ctx.checker, n.expression) : null;
      const returned = inner ? returnedExpression(inner) : null;
      const value = returned ? stringValue(returned, ctx.checker) : null;
      // Exactly one runtime part, at the end: the parameter itself.
      if (value && value.endsWith(RUNTIME) && value.indexOf(RUNTIME) === value.length - RUNTIME.length && value.length > RUNTIME.length)
        found = value.slice(0, -RUNTIME.length);
    }
    ts.forEachChild(n, visit);
  };
  visit(fn.body);
  prefixes.set(fn, found);
  return found;
}

function declaredFunction(checker: ts.TypeChecker, callee: ts.Expression): ts.FunctionLikeDeclaration | null {
  let symbol = checker.getSymbolAtLocation(ts.isPropertyAccessExpression(callee) ? callee.name : callee);
  if (symbol && symbol.flags & ts.SymbolFlags.Alias) {
    try { symbol = checker.getAliasedSymbol(symbol); } catch { return null; }
  }
  const d = symbol?.declarations?.length === 1 ? symbol.declarations[0] : null;
  if (!d) return null;
  if (ts.isFunctionDeclaration(d)) return d;
  if (ts.isVariableDeclaration(d) && d.initializer && (ts.isArrowFunction(d.initializer) || ts.isFunctionExpression(d.initializer))) return d.initializer;
  return null;
}

function returnedExpression(fn: ts.FunctionLikeDeclaration): ts.Expression | null {
  if (!fn.body) return null;
  if (!ts.isBlock(fn.body)) return fn.body;
  const returns = fn.body.statements.filter(ts.isReturnStatement);
  return returns.length === 1 ? returns[0].expression ?? null : null;
}

/** `#N` → the call's Nth argument; anything else → that property of the object in the first argument. */
function wrapperValue(n: ts.CallExpression, prop: string, checker: ts.TypeChecker): string | null {
  if (/^#\d+$/.test(prop)) return stringValue(n.arguments[Number(prop.slice(1))], checker);
  const arg = n.arguments[0];
  return arg && ts.isObjectLiteralExpression(arg) ? stringValue(objProp(arg, prop) ?? undefined, checker) : null;
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
    const url0 = w.urlProp ? wrapperValue(n, w.urlProp, checker) : stringValue(n.arguments[0], checker);
    let url: string | null = url0;
    const v = w.verbProp ? wrapperValue(n, w.verbProp, checker) : null;
    const verb = v ? v.toUpperCase() : w.verb;
    // D175: where the verb came from: written at the call, given in the setting (`@VERB`), or read off the wrapper's name.
    const verbFrom = v && !v.includes(RUNTIME) ? "literal" : w.fixed ? "setting" : "name";
    // Wrappers add the API root themselves (Sonarr: getQueryPath(path) = urlBase + apiRoot + path). When the
    // wrapper's own code shows that root, it is added (G2); otherwise a relative path is matched against the
    // end of endpoint paths.
    const rooted = (u: string) => {
      if (/^(https?:|\{base\})/.test(u)) return u;
      const prefix = w.urlProp ? wrapperPrefix(ctx, callee, w.urlProp) : null;
      return prefix ? `${prefix.replace(/\/$/, "")}/${u.replace(/^\//, "")}` : BASE + u.replace(/^\//, "");
    };
    if (url !== null) url = rooted(url);
    if (blind(url) && fromParameters(ctx, owner, file, line, w.urlProp ? wrapperExpr(n, w.urlProp) : n.arguments[0],
        w.verbProp ? wrapperExpr(n, w.verbProp) : undefined, w.verb, w.fixed ? "setting" : "name", rooted, `wrapper:${w.fn}`, typeArg(ctx, n, 0), typeArg(ctx, n, 1))) return;
    emit(ctx, owner, file, line, verb, url, `wrapper:${w.fn}`, typeArg(ctx, n, 0), typeArg(ctx, n, 1), verbFrom);
    return;
  }

  // Angular HttpClient: this.http.get<T>(url, …), post<T>(url, body), request(verb, url).
  if (receiver && (ANGULAR_VERBS[name] || name === "request") && isHttpClient(ctx, receiver)) {
    const [verb, urlArg, bodyArg] = name === "request"
      ? [stringValue(n.arguments[0], checker)?.toUpperCase() ?? "ANY", n.arguments[1], n.arguments[2]]
      : [ANGULAR_VERBS[name], n.arguments[0], n.arguments[1]];
    emit(ctx, owner, file, line, verb, stringValue(urlArg, checker), ctx.typed ? "angular-http" : "angular-http-by-name",
      typeArg(ctx, n, 0), name === "post" || name === "put" || name === "patch" ? valueType(ctx, bodyArg) : null,
      name !== "request" ? "library" : verb === "ANY" ? "runtime" : "literal");
    return;
  }

  // fetch(url, { method }) and axios.get/post/…(url) or axios({ url, method }).
  if (ts.isIdentifier(callee) && name === "fetch") {
    const opts = n.arguments[1];
    const method = opts && ts.isObjectLiteralExpression(opts) ? objProp(opts, "method") : null;
    const written = method ? stringValue(method, checker)?.toUpperCase() ?? null : null;
    // No options, or options without `method`: fetch's own default, GET. Options or a method the code doesn't show are
    // still matched as GET, as before, but the verb is unknown (D175).
    const unknown = (method && !written) || (opts && !ts.isObjectLiteralExpression(opts));
    const verb = written ?? "GET";
    emit(ctx, owner, file, line, verb, stringValue(n.arguments[0], checker), "fetch", typeArg(ctx, n, 0), null,
      unknown ? "runtime" : method ? "literal" : "library");
    return;
  }
  if (receiver && ts.isIdentifier(receiver) && receiver.text === "axios" && ANGULAR_VERBS[name]) {
    emit(ctx, owner, file, line, ANGULAR_VERBS[name], stringValue(n.arguments[0], checker), "axios", typeArg(ctx, n, 0),
      name === "post" || name === "put" || name === "patch" ? valueType(ctx, n.arguments[1]) : null, "library");
    return;
  }

  // N5: jQuery: $.ajax({ url, type | method }), $.ajax(url, { type }), $.get/post/getJSON(url).
  if (receiver && ts.isIdentifier(receiver) && (receiver.text === "$" || receiver.text === "jQuery")) {
    const first = n.arguments[0];
    if (name === "ajax") {
      const settings = first && ts.isObjectLiteralExpression(first) ? first : n.arguments[1];
      const urlArg = first && ts.isObjectLiteralExpression(first) ? objProp(first, "url") ?? undefined : first;
      const verbArg = settings && ts.isObjectLiteralExpression(settings) ? objProp(settings, "type") ?? objProp(settings, "method") : null;
      const verb = verbArg ? stringValue(verbArg, checker)?.toUpperCase() ?? "ANY" : "GET";
      emit(ctx, owner, file, line, verb, stringValue(urlArg, checker), "jquery", null, null,
        verb === "ANY" ? "runtime" : verbArg ? "literal" : "library");
      return;
    }
    const JQUERY_VERBS: Record<string, string> = { get: "GET", post: "POST", getJSON: "GET" };
    if (JQUERY_VERBS[name] && first) {
      emit(ctx, owner, file, line, JQUERY_VERBS[name], stringValue(first, checker), "jquery", null, null, "library");
      return;
    }
  }

  // A generated API client whose file isn't in the repo (NSwag writes it at build time).
  if (receiver) {
    const client = generatedClientName(ctx, receiver);
    if (client) ctx.out.fact(DETECTOR, "generated-call", owner, file, line, { client, method: name, generator: "nswag" });
  }
}

/** `#N` → the call's Nth argument; anything else → that property of the object in the first argument: the expression itself. */
function wrapperExpr(n: ts.CallExpression, prop: string): ts.Expression | undefined {
  if (/^#\d+$/.test(prop)) return n.arguments[Number(prop.slice(1))];
  const arg = n.arguments[0];
  return arg && ts.isObjectLiteralExpression(arg) ? objProp(arg, prop) ?? undefined : undefined;
}

function blind(url: string | null): boolean {
  return url === null || url === RUNTIME || url.replace(BASE, "").replace(/\{\*\}/g, "").replace(/[/?&=]/g, "") === "";
}

/**
 * D190: a URL the call's own code doesn't show, built from its function's parameters: one `call` fact per value the callers
 * pass (`from` "parameter", `origin` the point that gave it, `via` the points it passed through, `of` how many), each verb
 * paired with the URLs written under the same side of a `?:`. False when no caller's value is known, so the call stays ❓.
 */
function fromParameters(ctx: Context, owner: string, file: string, line: number, urlExpr: ts.Expression | undefined, verbExpr: ts.Expression | undefined,
                        defaultVerb: string, defaultFrom: string, rooted: (u: string) => string, via: string, responseType: string | null, bodyType: string | null): boolean {
  const urls = paramValues(ctx, urlExpr);
  if (!urls) return false;
  const verbs = verbExpr ? valuesOf(ctx, verbExpr) : null;
  const combos: { verb: string; verbFrom: string; url: string; origin: string; path: string[] }[] = [];
  for (const u of urls) {
    for (const v of verbs ?? [{ value: defaultVerb, origin: null, via: [], when: {} }]) {
      const both = join(u, { ...v, value: "" });
      if (!both) continue;
      const known = verbs !== null && !v.value.includes(RUNTIME);
      const url = rooted(u.value);
      if (blind(url)) continue;
      combos.push({ verb: known ? v.value.toUpperCase() : defaultVerb, verbFrom: known ? "literal" : verbs ? "runtime" : defaultFrom, url, origin: both.origin ?? "", path: both.via });
    }
  }
  if (combos.length === 0) return false;
  const values = new Set(combos.map((c) => c.url.split("?")[0])).size;
  combos.sort((a, b) => (a.verb + a.url + a.origin < b.verb + b.url + b.origin ? -1 : a.verb + a.url + a.origin > b.verb + b.url + b.origin ? 1 : 0));
  const seen = new Set<string>();
  for (const c of combos) {
    const key = `${c.verb} ${c.url.split("?")[0]} ${c.origin}`;
    if (seen.has(key)) continue;
    seen.add(key);
    ctx.out.fact(DETECTOR, "call", owner, file, line, {
      verb: c.verb, url: c.url.split("?")[0], via, responseType: responseType ?? "", bodyType: bodyType ?? "", verbFrom: c.verbFrom,
      from: "parameter", origin: c.origin, path: c.path.join("|"), of: String(values),
    });
  }
  return true;
}

function emit(ctx: Context, owner: string, file: string, line: number, verb: string, url: string | null, via: string,
              responseType: string | null, bodyType: string | null, verbFrom: string) {
  if (url === null || url === RUNTIME || url.replace(BASE, "").replace(/\{\*\}/g, "").replace(/[/?&=]/g, "") === "") {
    ctx.out.blind(owner, file, line, `HTTP ${verb} to a URL built at runtime`, DETECTOR);
    return;
  }
  ctx.out.fact(DETECTOR, "call", owner, file, line, {
    verb, url: url.split("?")[0], via, responseType: responseType ?? "", bodyType: bodyType ?? "", verbFrom,
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

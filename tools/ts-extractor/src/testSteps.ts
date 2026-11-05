// A Playwright spec's steps, in order, by parsing only (D149). Nothing is run and no types are read, so this works on a file alone.
// What can't be followed (a helper, a page object, a computed selector, a loop, a condition) is recorded as a step with a reason and
// never guessed at. The words are the test's own. Bundled apart from the graph's extractor, so the graph's hashes don't move with it.
import ts from "typescript";

export interface Step {
  line: number;
  /** goto, click, fill, select, check, action, wait, read, or unknown when even the verb isn't known. */
  kind: string;
  /** The call as the test wrote it. */
  words: string;
  /** The locator, with a local `const` followed. */
  target?: string;
  /** A goto's URL when the whole argument is one string; null when it's built. */
  url?: string | null;
  /** The name just before a goto's dot, as the graph's reading has it: `page` in `this.page.goto`. */
  receiver?: string;
  /** Set when this step is ❓. A fixed sentence, so they can be counted. */
  reason?: string;
  /** For a loop or condition: how many statements inside weren't read. */
  inside?: number;
}

export interface Block {
  /** test, beforeEach, afterEach, beforeAll or afterAll. */
  kind: string;
  /** The describe titles around it, then its own, as written. */
  titles: string[];
  line: number;
  /** skip, fixme, fail or only, when the test says so. */
  mode: string | null;
  steps: Step[];
}

export interface TestFile {
  t: "test-file";
  file: string;
  blocks: Block[];
  /** Places outside any test that may hold or make tests, and aren't read. */
  unread: { line: number; words: string; reason: string }[];
  /** Steps known to act on a page. A file that names no Playwright import and has none of these is another runner's test. */
  pageSteps: number;
}

export const Reasons = {
  helper: "a helper function, which isn't followed",
  object: "a call on a page object or other value, which isn't followed",
  objectTarget: "its target comes from a page object or other value, which isn't followed",
  value: "it checks a value, not a locator on the page",
  selector: "a computed selector",
  loop: "a loop: what's inside may run any number of times, so it isn't read",
  condition: "a condition: what's inside may not run, so it isn't read",
  tryBlock: "a try block: what runs depends on what throws, so it isn't read",
  pageCall: "a page call this reader doesn't know",
  stops: "a skip inside a test may stop it here",
  callback: "the test's body is somewhere else, which isn't followed",
} as const;

const File = {
  call: "a call outside any test, which may define tests and isn't followed",
  loop: "a loop outside any test, which may make tests and isn't read",
  condition: "a condition outside any test, which may hold tests and isn't read",
  noBody: "a test with no function of its own",
  syntax: "the parser found syntax it couldn't read here, so steps may be missing",
} as const;

const Click = new Set(["click", "dblclick", "tap"]);
const Fill = new Set(["fill", "type", "pressSequentially"]);
const Select = new Set(["selectOption"]);
const Action = new Set(["press", "hover", "check", "uncheck", "setChecked", "clear", "focus", "blur", "dragTo", "setInputFiles", "selectText", "dispatchEvent", "scrollIntoViewIfNeeded", "reload", "goBack", "goForward", "bringToFront", "setViewportSize", "emulateMedia", "screenshot", "move", "down", "up", "wheel"]);
const Wait = new Set(["waitFor", "waitForURL", "waitForTimeout", "waitForLoadState", "waitForSelector", "waitForRequest", "waitForResponse", "waitForEvent", "waitForFunction", "waitForNavigation"]);
const Read = new Set(["count", "textContent", "innerText", "innerHTML", "inputValue", "getAttribute", "isVisible", "isHidden", "isChecked", "isEnabled", "isDisabled", "isEditable", "allTextContents", "allInnerTexts", "all", "title", "url", "content", "boundingBox"]);
/** Finds a place on the page and does nothing, so a `const` holding one is a name for it. */
const Builders = new Set(["locator", "first", "last", "nth", "filter", "and", "or", "frameLocator", "contentFrame", "owner", "getByRole", "getByText", "getByLabel", "getByPlaceholder", "getByAltText", "getByTitle", "getByTestId"]);
/** Opening and closing a browser, which isn't a step in the journey. */
const Setup = new Set(["newPage", "newContext", "close", "setDefaultTimeout", "setDefaultNavigationTimeout"]);
const Hooks = new Set(["beforeEach", "afterEach", "beforeAll", "afterAll"]);
const Modes = new Set(["only", "skip", "fixme", "fail"]);

function kindOf(verb: string): string | null {
  if (verb === "goto") return "goto";
  if (Click.has(verb)) return "click";
  if (Fill.has(verb)) return "fill";
  if (Select.has(verb)) return "select";
  if (Action.has(verb)) return "action";
  if (Wait.has(verb)) return "wait";
  if (Read.has(verb)) return "read";
  return null;
}

/** What a chain of calls hangs from. `page` is by name, as the graph's reading of a goto is. */
interface Chain {
  root: "page" | "other" | "none";
  computed: boolean;
  text: string;
  /** A bare name with nothing after it: `quantity`, not `home.quantity`. */
  bare: boolean;
}

type Scope = Map<string, Chain>;

class Reader {
  readonly blocks: Block[] = [];
  readonly unread: TestFile["unread"] = [];

  readonly sf: ts.SourceFile;

  constructor(sf: ts.SourceFile) {
    this.sf = sf;
  }

  lineOf(n: ts.Node): number {
    return this.sf.getLineAndCharacterOfPosition(n.getStart(this.sf)).line + 1;
  }

  /** The test's words, with only the line breaks and indents between them closed up. */
  words(n: ts.Node): string {
    return tidy(n.getText(this.sf));
  }

  // Outside any test: tests, describes, hooks, and what might hide some.

  container(statements: readonly ts.Statement[], scope: Scope, titles: string[]) {
    for (const s of statements) {
      if (ts.isVariableStatement(s)) this.remember(s, scope);
      else if (ts.isExpressionStatement(s) && ts.isCallExpression(strip(s.expression))) this.topCall(strip(s.expression) as ts.CallExpression, scope, titles);
      else if (ts.isIterationStatement(s, false)) this.unread.push({ line: this.lineOf(s), words: this.header(s), reason: File.loop });
      else if (ts.isIfStatement(s) || ts.isSwitchStatement(s) || ts.isTryStatement(s)) this.unread.push({ line: this.lineOf(s), words: this.header(s), reason: File.condition });
    }
  }

  topCall(call: ts.CallExpression, scope: Scope, titles: string[]) {
    const path = dotted(call.expression);
    if (path?.[0] !== "test") {
      this.unread.push({ line: this.lineOf(call), words: this.words(call), reason: File.call });
      return;
    }
    const parts = path.slice(1);
    if (parts[0] === "describe") {
      if (parts.includes("configure")) return;
      const fn = bodyOf(call);
      if (!fn) {
        this.unread.push({ line: this.lineOf(call), words: this.words(call), reason: File.noBody });
        return;
      }
      const title = call.arguments.length > 1 ? this.title(call.arguments[0]) : null;
      if (ts.isBlock(fn.body)) this.container(fn.body.statements, new Map(scope), title === null ? titles : [...titles, title]);
      return;
    }
    if (Hooks.has(parts[0])) {
      const fn = bodyOf(call);
      const title = call.arguments.length > 1 ? this.title(call.arguments[0]) : null;
      this.block(parts[0], title === null ? titles : [...titles, title], call, null, fn, scope);
      return;
    }
    // `test.use`, `test.slow()` and the like set options outside any test. They walk through nothing.
    if (parts.length > 0 && !Modes.has(parts[0])) return;
    if (call.arguments.length === 0 || parts.length > 0 && call.arguments.length < 2 && !isFunctionLike(call.arguments[0])) return; // `test.skip()` / `test.skip(cond)` at file level: an option.
    const fn = bodyOf(call);
    this.block("test", [...titles, this.title(call.arguments[0])], call, parts[0] ?? null, fn, scope);
  }

  block(kind: string, titles: string[], call: ts.CallExpression, mode: string | null, fn: ts.ArrowFunction | ts.FunctionExpression | null, scope: Scope) {
    const steps: Step[] = [];
    if (fn) this.body(fn, scope, steps);
    else steps.push({ line: this.lineOf(call), kind: "unknown", words: this.words(call), reason: Reasons.callback });
    this.blocks.push({ kind, titles, line: this.lineOf(call), mode, steps });
  }

  title(n: ts.Expression): string {
    return ts.isStringLiteralLike(n) ? n.text : this.words(n);
  }

  // Inside a test: statements in order.

  body(fn: ts.ArrowFunction | ts.FunctionExpression, scope: Scope, out: Step[]) {
    if (ts.isBlock(fn.body)) this.statements(fn.body.statements, new Map(scope), out);
    else this.expression(fn.body, new Map(scope), out, true);
  }

  statements(list: readonly ts.Statement[], scope: Scope, out: Step[]) {
    for (const s of list) this.statement(s, scope, out);
  }

  statement(s: ts.Statement, scope: Scope, out: Step[]) {
    if (ts.isBlock(s)) this.statements(s.statements, new Map(scope), out);
    else if (ts.isExpressionStatement(s)) this.expression(s.expression, scope, out, true);
    else if (ts.isVariableStatement(s)) {
      // A name for a place on the page does nothing yet. Read the rest before remembering, since a name can't mean itself.
      for (const d of s.declarationList.declarations) if (d.initializer && !isName(d.initializer, this, scope)) this.expression(d.initializer, scope, out, false);
      this.remember(s, scope);
    } else if (ts.isReturnStatement(s)) {
      if (s.expression) this.expression(s.expression, scope, out, true);
    } else if (ts.isIterationStatement(s, false)) this.blindBlock(s, Reasons.loop, out);
    else if (ts.isIfStatement(s) || ts.isSwitchStatement(s)) this.blindBlock(s, Reasons.condition, out);
    else if (ts.isTryStatement(s)) this.blindBlock(s, Reasons.tryBlock, out);
    else if (ts.isLabeledStatement(s)) this.statement(s.statement, scope, out);
  }

  blindBlock(s: ts.Statement, reason: string, out: Step[]) {
    out.push({ line: this.lineOf(s), kind: "unknown", words: this.header(s), reason, inside: countStatements(s) });
  }

  /** A loop's or condition's opening line: what decides, and none of what's inside. */
  header(s: ts.Statement): string {
    let body: ts.Node | undefined;
    if (ts.isIfStatement(s)) body = s.thenStatement;
    else if (ts.isSwitchStatement(s)) body = s.caseBlock;
    else if (ts.isTryStatement(s)) return "try";
    else if (ts.isIterationStatement(s, false)) body = s.statement;
    if (ts.isDoStatement(s)) return `do … while (${this.words(s.expression)})`;
    return body ? tidy(this.sf.text.slice(s.getStart(this.sf), body.getStart(this.sf))).trim() : this.words(s);
  }

  /** A `const` holding a place on the page is a name for that place. `let` isn't: it can change. */
  remember(s: ts.VariableStatement, scope: Scope) {
    if (!(s.declarationList.flags & ts.NodeFlags.Const)) {
      for (const d of s.declarationList.declarations) if (ts.isIdentifier(d.name)) scope.delete(d.name.text);
      return;
    }
    for (const d of s.declarationList.declarations) {
      if (!ts.isIdentifier(d.name) || !d.initializer) continue;
      if (isName(d.initializer, this, scope)) scope.set(d.name.text, this.chain(d.initializer, scope));
      else scope.delete(d.name.text);
    }
  }

  expression(raw: ts.Expression, scope: Scope, out: Step[], mustRecord: boolean) {
    const awaited = isAwaited(raw);
    const e = strip(raw);
    if (ts.isCallExpression(e)) return this.call(e, scope, out, mustRecord || awaited);
    if (ts.isConditionalExpression(e) || (ts.isBinaryExpression(e) && [ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(e.operatorToken.kind))) {
      if (holdsCall(e)) out.push({ line: this.lineOf(e), kind: "unknown", words: this.words(e), reason: Reasons.condition });
      return;
    }
    if (ts.isBinaryExpression(e) && e.operatorToken.kind === ts.SyntaxKind.EqualsToken) return this.expression(e.right, scope, out, false);
    if (isFunctionLike(e) || ts.isNewExpression(e) || ts.isIdentifier(e)) return;
    // Anything else (an object, an array, a sum): only what it awaits acts, so look for that and don't go into functions.
    ts.forEachChild(e, (c) => this.lookForAwaits(c, scope, out));
  }

  lookForAwaits(n: ts.Node, scope: Scope, out: Step[]) {
    if (isFunctionLike(n)) return;
    if (ts.isAwaitExpression(n)) return this.expression(n, scope, out, true);
    ts.forEachChild(n, (c) => this.lookForAwaits(c, scope, out));
  }

  call(call: ts.CallExpression, scope: Scope, out: Step[], mustRecord: boolean) {
    const line = this.lineOf(call);
    const words = this.words(call);
    if (this.isExpect(call)) return this.check(call, scope, out);

    const path = dotted(call.expression);
    if (path?.[0] === "Promise" && ["all", "race", "allSettled", "any"].includes(path[1] ?? "")) {
      const list = call.arguments[0];
      if (list && ts.isArrayLiteralExpression(list)) for (const x of list.elements) this.expression(x, scope, out, true);
      else out.push({ line, kind: "unknown", words, reason: Reasons.object });
      return;
    }
    if (path?.[0] === "console") return;
    if (path?.[0] === "test") {
      if (path[1] === "step") {
        const fn = bodyOf(call);
        if (fn) this.body(fn, scope, out);
        else out.push({ line, kind: "unknown", words, reason: Reasons.callback });
      } else if (path[1] === "skip" || path[1] === "fixme") out.push({ line, kind: "unknown", words, reason: Reasons.stops });
      return; // test.setTimeout, test.slow, test.info: options.
    }

    const callee = strip(call.expression);
    if (ts.isIdentifier(callee)) {
      if (mustRecord || passesPage(call)) out.push({ line, kind: "unknown", words, reason: Reasons.helper });
      return;
    }
    if (!ts.isPropertyAccessExpression(callee)) {
      if (mustRecord || passesPage(call)) out.push({ line, kind: "unknown", words, reason: Reasons.object });
      return;
    }
    const verb = callee.name.text;
    const on = this.chain(callee.expression, scope);
    if (Setup.has(verb)) return;
    const kind = kindOf(verb);

    if (kind === "goto") {
      const arg = call.arguments[0];
      out.push({ line, kind, words, url: arg && ts.isStringLiteralLike(arg) ? arg.text : null, receiver: receiverName(callee.expression) });
      return;
    }
    if (on.root === "page") {
      if (kind === null) {
        if (Builders.has(verb) && !mustRecord) return;
        out.push({ line, kind: "unknown", words, reason: Reasons.pageCall });
        return;
      }
      const step: Step = { line, kind, words, target: on.text };
      // `page.click(x)`: on the page itself, the first argument is the selector. A wait's is usually a function, and that's no place on the page.
      const selector = on.text === "page" && kind !== "wait" && kind !== "read" && !Action.has(verb) ? call.arguments[0] : undefined;
      if (on.computed || (selector && !this.plain(selector, scope))) step.reason = Reasons.selector;
      out.push(step);
      return;
    }
    if (!mustRecord && !passesPage(call)) return;
    if (kind !== null && kind !== "read") {
      out.push({ line, kind, words, target: on.text, reason: Reasons.objectTarget });
      return;
    }
    out.push({ line, kind: "unknown", words, reason: Reasons.object });
  }

  isExpect(call: ts.CallExpression): boolean {
    return this.expectSubject(call) !== null;
  }

  /** The innermost `expect(x)` of `expect(x).not.toBe(y)`, when this call is the check itself. */
  expectSubject(call: ts.CallExpression): ts.CallExpression | null {
    const callee = strip(call.expression);
    if (!ts.isPropertyAccessExpression(callee)) return null;
    let at: ts.Expression = callee.expression;
    while (ts.isPropertyAccessExpression(at)) at = at.expression;
    at = strip(at);
    if (!ts.isCallExpression(at)) return null;
    const head = dotted(at.expression);
    return head?.[0] === "expect" && (head.length === 1 || head[1] === "soft" || head[1] === "poll") ? at : null;
  }

  check(call: ts.CallExpression, scope: Scope, out: Step[]) {
    const expectCall = this.expectSubject(call)!;
    const subject = expectCall.arguments[0];
    const line = this.lineOf(call);
    const words = this.words(call);
    if (!subject) {
      out.push({ line, kind: "check", words, reason: Reasons.value });
      return;
    }
    const chain = this.chain(subject, scope);
    const step: Step = { line, kind: "check", words, target: chain.text };
    if (chain.root === "page") {
      if (chain.computed) step.reason = Reasons.selector;
    } else if (chain.root === "other" && !chain.bare) step.reason = Reasons.objectTarget;
    else step.reason = Reasons.value;
    out.push(step);
  }

  /** What an expression hangs from, with a local name for a place on the page followed. */
  chain(raw: ts.Expression, scope: Scope): Chain {
    const e = strip(raw);
    if (ts.isIdentifier(e)) {
      if (scope.has(e.text)) return scope.get(e.text)!;
      return e.text === "page" ? { root: "page", computed: false, text: "page", bare: true } : { root: "other", computed: false, text: e.text, bare: true };
    }
    if (ts.isPropertyAccessExpression(e)) {
      if (e.expression.kind === ts.SyntaxKind.ThisKeyword) return e.name.text === "page" ? { root: "page", computed: false, text: "this.page", bare: true } : { root: "other", computed: false, text: this.words(e), bare: false };
      const inner = this.chain(e.expression, scope);
      return { ...inner, text: `${inner.text}.${e.name.text}`, bare: false };
    }
    if (ts.isElementAccessExpression(e)) {
      const inner = this.chain(e.expression, scope);
      return { ...inner, computed: inner.computed || !this.plain(e.argumentExpression, scope), text: `${inner.text}[${this.words(e.argumentExpression)}]`, bare: false };
    }
    if (ts.isCallExpression(e)) {
      const inner = this.chain(e.expression, scope);
      const args = e.arguments.map((a) => this.words(a)).join(", ");
      return { ...inner, computed: inner.computed || !e.arguments.every((a) => this.plain(a, scope)), text: `${inner.text}(${args})`, bare: false };
    }
    return { root: "none", computed: true, text: this.words(e), bare: false };
  }

  /** An argument whose whole value is in the test's words: a string, regex, number, or an object of those, or another place on the page. */
  plain(raw: ts.Expression, scope: Scope): boolean {
    const e = strip(raw);
    if (ts.isStringLiteralLike(e) || ts.isNumericLiteral(e) || ts.isRegularExpressionLiteral(e)) return true;
    if ([ts.SyntaxKind.TrueKeyword, ts.SyntaxKind.FalseKeyword, ts.SyntaxKind.NullKeyword].includes(e.kind)) return true;
    if (ts.isPrefixUnaryExpression(e)) return this.plain(e.operand, scope);
    if (ts.isArrayLiteralExpression(e)) return e.elements.every((x) => this.plain(x, scope));
    if (ts.isObjectLiteralExpression(e)) return e.properties.every((p) => ts.isPropertyAssignment(p) && !ts.isComputedPropertyName(p.name) && this.plain(p.initializer, scope));
    const chain = this.chain(e, scope);
    return chain.root === "page" && !chain.computed;
  }
}

/** A `const x = page.locator('a')` names a place, and isn't a step. */
function isName(init: ts.Expression, reader: Reader, scope: Scope): boolean {
  if (isAwaited(init)) return false;
  const e = strip(init);
  if (!ts.isCallExpression(e)) return false;
  const callee = strip(e.expression);
  return ts.isPropertyAccessExpression(callee) && Builders.has(callee.name.text) && reader.chain(callee.expression, scope).root === "page";
}

function tidy(text: string): string {
  return text.replace(/\s*\n\s*/g, " ");
}

function isAwaited(e: ts.Expression): boolean {
  let at = e;
  while (ts.isParenthesizedExpression(at) || ts.isNonNullExpression(at) || ts.isAsExpression(at) || ts.isSatisfiesExpression(at)) at = at.expression;
  return ts.isAwaitExpression(at);
}

function strip(e: ts.Expression): ts.Expression {
  let at = e;
  while (ts.isParenthesizedExpression(at) || ts.isNonNullExpression(at) || ts.isAsExpression(at) || ts.isSatisfiesExpression(at) || ts.isAwaitExpression(at)) at = at.expression;
  return at;
}

function isFunctionLike(n: ts.Node): n is ts.ArrowFunction | ts.FunctionExpression {
  return ts.isArrowFunction(n) || ts.isFunctionExpression(n);
}

/** `test.describe.only` as ["test", "describe", "only"]; null if it isn't only names. */
function dotted(raw: ts.Expression): string[] | null {
  const e = strip(raw);
  if (ts.isIdentifier(e)) return [e.text];
  if (ts.isPropertyAccessExpression(e)) {
    const head = dotted(e.expression);
    return head ? [...head, e.name.text] : null;
  }
  return null;
}

/** The name just before the dot, as the graph's reading has it. Empty for anything else, like `(await open()).goto`. */
function receiverName(raw: ts.Expression): string {
  if (raw.kind === ts.SyntaxKind.ThisKeyword) return "this";
  if (ts.isIdentifier(raw)) return raw.text;
  return ts.isPropertyAccessExpression(raw) ? raw.name.text : "";
}

function bodyOf(call: ts.CallExpression): ts.ArrowFunction | ts.FunctionExpression | null {
  const last = call.arguments[call.arguments.length - 1];
  return last && isFunctionLike(last) ? last : null;
}

function passesPage(call: ts.CallExpression): boolean {
  return call.arguments.some((a) => {
    const e = strip(a);
    return ts.isIdentifier(e) && e.text === "page";
  });
}

function holdsCall(n: ts.Node): boolean {
  if (ts.isCallExpression(n)) return true;
  return ts.forEachChild(n, (c) => (isFunctionLike(c) ? false : holdsCall(c))) === true;
}

function countStatements(s: ts.Node): number {
  let n = 0;
  const visit = (x: ts.Node) => {
    if (x !== s && (ts.isExpressionStatement(x) || ts.isVariableStatement(x) || ts.isReturnStatement(x))) n++;
    if (!isFunctionLike(x)) ts.forEachChild(x, visit);
  };
  visit(s);
  return n;
}

/** One spec file's tests and steps, read from its text. */
export function readTestFile(file: string, text: string): TestFile {
  const sf = ts.createSourceFile(file, text, ts.ScriptTarget.ES2022, true);
  const reader = new Reader(sf);
  reader.container(sf.statements, new Map(), []);
  const diagnostics = (sf as unknown as { parseDiagnostics?: ts.DiagnosticWithLocation[] }).parseDiagnostics ?? [];
  if (diagnostics.length > 0)
    reader.unread.push({ line: sf.getLineAndCharacterOfPosition(diagnostics[0].start).line + 1, words: diagnostics[0].messageText.toString(), reason: File.syntax });
  const pageSteps = reader.blocks.flatMap((b) => b.steps).filter((s) => s.kind !== "unknown" && s.target?.startsWith("page") || (s.kind === "goto" && s.receiver === "page")).length;
  return { t: "test-file", file, blocks: reader.blocks, unread: reader.unread, pageSteps };
}

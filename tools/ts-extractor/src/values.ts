import ts from "typescript";

/** Marks a part of a URL known only at runtime; it matches any one path segment. */
export const RUNTIME = "{*}";
/** Marks a base URL prefix (environment.apiUrl, this.baseUrl, apiRoot…): matched against the end of an endpoint's path. */
export const BASE = "{base}";

const BASE_NAMES = /(^|\.)(base|api|root|url|host|endpoint|server)[a-z]*$/i;

/**
 * The string an expression evaluates to, as far as the code shows it: literals, template
 * literals, `+` concatenation, and constants or `readonly`/single-assignment variables followed
 * to their initialiser. Parts known only at runtime become {*}; a leading base-URL variable
 * becomes {base}. Null when nothing at all is known.
 */
export function stringValue(expr: ts.Expression | undefined, checker: ts.TypeChecker, depth = 0): string | null {
  if (!expr || depth > 6) return null;
  expr = skipOuter(expr);
  if (ts.isStringLiteral(expr) || ts.isNoSubstitutionTemplateLiteral(expr)) return expr.text;
  if (ts.isNumericLiteral(expr)) return expr.text;
  if (ts.isTemplateExpression(expr)) {
    let s = expr.head.text;
    expr.templateSpans.forEach((span, i) => {
      s += part(span.expression, checker, depth, i === 0 && expr.head.text === "") + span.literal.text;
    });
    return s;
  }
  if (ts.isBinaryExpression(expr) && expr.operatorToken.kind === ts.SyntaxKind.PlusToken) {
    const left = part(expr.left, checker, depth, true);
    const right = part(expr.right, checker, depth, false);
    return left === RUNTIME && right === RUNTIME ? null : left + right;
  }
  if (ts.isIdentifier(expr) || ts.isPropertyAccessExpression(expr)) {
    const init = initializer(expr, checker);
    return init ? stringValue(init, checker, depth + 1) : null;
  }
  if (ts.isCallExpression(expr) && ts.isPropertyAccessExpression(expr.expression)
      && ["replace", "trim", "toLowerCase", "concat"].includes(expr.expression.name.text)) {
    // url_ = url_.replace(/[?&]$/, "") and friends keep the path.
    const inner = stringValue(expr.expression.expression, checker, depth + 1);
    if (expr.expression.name.text !== "concat" || inner === null) return inner;
    return inner + expr.arguments.map((a) => part(a, checker, depth, false)).join("");
  }
  return null;
}

/** One piece of a concatenation: its value, {base} for a leading base-URL name, or {*}. */
function part(expr: ts.Expression, checker: ts.TypeChecker, depth: number, leading: boolean): string {
  const value = stringValue(expr, checker, depth + 1);
  if (value !== null) return value;
  if (leading && isBaseName(expr)) return BASE;
  return RUNTIME;
}

export function isBaseName(expr: ts.Expression): boolean {
  expr = skipOuter(expr);
  const text = ts.isIdentifier(expr) ? expr.text : ts.isPropertyAccessExpression(expr) ? expr.name.text : null;
  return !!text && BASE_NAMES.test(text);
}

function skipOuter(expr: ts.Expression): ts.Expression {
  while (ts.isParenthesizedExpression(expr) || ts.isAsExpression(expr) || ts.isNonNullExpression(expr)
         || ts.isTypeAssertionExpression(expr) || ts.isSatisfiesExpression(expr)) expr = expr.expression;
  return expr;
}

/** The single initialiser of a const, readonly property, or variable assigned once. */
function initializer(expr: ts.Identifier | ts.PropertyAccessExpression, checker: ts.TypeChecker): ts.Expression | null {
  let symbol = checker.getSymbolAtLocation(ts.isIdentifier(expr) ? expr : expr.name);
  if (symbol && symbol.flags & ts.SymbolFlags.Alias) {
    try { symbol = checker.getAliasedSymbol(symbol); } catch { return null; }
  }
  const decls = symbol?.declarations ?? [];
  if (decls.length !== 1) return null;
  const d = decls[0];
  if ((ts.isVariableDeclaration(d) || ts.isPropertyDeclaration(d)) && d.initializer) {
    if (ts.isVariableDeclaration(d) && !(ts.getCombinedNodeFlags(d) & ts.NodeFlags.Const) && reassigned(d, checker)) return null;
    return d.initializer;
  }
  return null;
}

/** `let url_ = …; url_ = url_.replace(…)` counts as assigned once: self-based updates keep the value's shape. */
function reassigned(d: ts.VariableDeclaration, checker: ts.TypeChecker): boolean {
  if (!ts.isIdentifier(d.name)) return true;
  const symbol = checker.getSymbolAtLocation(d.name);
  const scope = d.parent.parent.parent;
  let other = false;
  const visit = (n: ts.Node) => {
    if (other) return;
    if (ts.isBinaryExpression(n) && n.operatorToken.kind === ts.SyntaxKind.EqualsToken && ts.isIdentifier(n.left)
        && checker.getSymbolAtLocation(n.left) === symbol) {
      const selfBased = ts.isCallExpression(n.right) && ts.isPropertyAccessExpression(n.right.expression)
        && ts.isIdentifier(n.right.expression.expression) && checker.getSymbolAtLocation(n.right.expression.expression) === symbol;
      if (!selfBased) other = true;
    }
    ts.forEachChild(n, visit);
  };
  visit(scope);
  return other;
}

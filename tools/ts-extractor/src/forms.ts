// D209: Angular signal forms' rules. `form(this.model, (path) => { required(path.username); })`: each rule function from
// @angular/forms/signals called in the schema with a path into the model is a `rule` fact on the field holding the form, naming
// the property it checks and the field holding the model, so a flow that reads either lists the rules as its checks.
import ts from "typescript";
import type { Context } from "./extract.ts";

const MODULE = "@angular/forms/signals";
/** Schema functions that only group or reuse rules: not rules themselves. */
const NOT_RULES = new Set(["form", "schema", "apply", "applyEach", "applyWhen", "applyWhenValue", "disabled", "hidden", "readonly", "debounce", "submit", "metadata"]);

/** The name an identifier was imported as from a module, or null. */
function importedFrom(ctx: Context, id: ts.Identifier, module: string): string | null {
  const symbol = ctx.checker.getSymbolAtLocation(id);
  const decl = symbol?.declarations?.[0];
  if (!decl || !ts.isImportSpecifier(decl)) return null;
  const from = decl.parent.parent.parent.moduleSpecifier;
  return ts.isStringLiteral(from) && from.text === module ? (decl.propertyName ?? decl.name).text : null;
}

export function signalFormRules(ctx: Context, sf: ts.SourceFile) {
  const visit = (n: ts.Node): void => {
    if (ts.isCallExpression(n) && ts.isIdentifier(n.expression) && importedFrom(ctx, n.expression, MODULE) === "form" && n.arguments.length >= 2) {
      const schema = n.arguments[1];
      const param = (ts.isArrowFunction(schema) || ts.isFunctionExpression(schema)) ? schema.parameters[0]?.name : undefined;
      if (param && ts.isIdentifier(param)) {
        const owner = ctx.ownerId(n);
        const model = n.arguments[0];
        const modelId = ctx.symbolId(ctx.checker.getSymbolAtLocation(ts.isPropertyAccessExpression(model) ? model.name : model)) ?? "";
        const rules = (m: ts.Node): void => {
          if (ts.isCallExpression(m) && ts.isIdentifier(m.expression)) {
            const name = importedFrom(ctx, m.expression, MODULE);
            const target = m.arguments[0];
            const property = target ? pathOf(target, param.text) : null;
            if (name && !NOT_RULES.has(name) && property !== null) {
              ctx.out.fact("validation", "rule", owner, ctx.rel(sf.fileName), ctx.line(m), {
                rule: name, args: m.arguments.slice(1).map((a) => (ts.isArrowFunction(a) || ts.isFunctionExpression(a) ? "a custom check" : a.getText())).join(", "), via: "angular-signal-forms", set: "",
                message: "", conditional: "", property, model: modelId,
              });
            }
          }
          ts.forEachChild(m, rules);
        };
        ts.forEachChild(schema.body, rules);
      }
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

/** `path.address.city` → `address.city`; `path` itself → ``; anything else → null. */
function pathOf(e: ts.Expression, root: string): string | null {
  if (ts.isIdentifier(e)) return e.text === root ? "" : null;
  if (ts.isPropertyAccessExpression(e)) {
    const head = pathOf(e.expression, root);
    return head === null ? null : head === "" ? e.name.text : `${head}.${e.name.text}`;
  }
  return null;
}

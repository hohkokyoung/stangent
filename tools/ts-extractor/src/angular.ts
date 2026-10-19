import * as fs from "node:fs";
import * as path from "node:path";
import ts from "typescript";
import { parseTemplate } from "@angular/compiler";
import type { Context } from "./extract.ts";
import { isAngularComponent } from "./extract.ts";

export interface AngularComponent {
  id: string;
  cls: ts.ClassDeclaration;
  members: Set<string>;
  template: { text: string; file: string; line: number; external: boolean } | null;
}

export interface AngularIndex {
  components: Map<string, AngularComponent>;
  /** Element selector (app-series-card) or attribute selector ([appTooltip]) → component/directive ids. */
  selectors: Map<string, string[]>;
  /** Pipe name (date, sentenceCase) → pipe class ids. */
  pipes: Map<string, string[]>;
}

const DECORATORS = new Set(["Component", "Directive", "Pipe"]);

function decoratorArg(cls: ts.ClassDeclaration, name: string): ts.ObjectLiteralExpression | null {
  for (const d of ts.getDecorators(cls) ?? []) {
    if (ts.isCallExpression(d.expression) && ts.isIdentifier(d.expression.expression) && d.expression.expression.text === name) {
      const arg = d.expression.arguments[0];
      return arg && ts.isObjectLiteralExpression(arg) ? arg : null;
    }
  }
  return null;
}

function prop(obj: ts.ObjectLiteralExpression, name: string): ts.Expression | null {
  for (const p of obj.properties)
    if (ts.isPropertyAssignment(p) && (ts.isIdentifier(p.name) || ts.isStringLiteral(p.name)) && p.name.text === name) return p.initializer;
  return null;
}

const str = (e: ts.Expression | null) => (e && (ts.isStringLiteral(e) || ts.isNoSubstitutionTemplateLiteral(e)) ? e.text : null);

/** First pass: every component, directive and pipe with its selector or name, so templates can link to them. */
export function collectAngular(ctx: Context, sf: ts.SourceFile) {
  const visit = (n: ts.Node) => {
    if (ts.isClassDeclaration(n) && n.name) {
      const id = ctx.declId(n);
      for (const kind of DECORATORS) {
        const arg = id ? decoratorArg(n, kind) : null;
        if (!arg || !id) continue;
        if (kind === "Pipe") {
          const name = str(prop(arg, "name"));
          if (name) push(ctx.angular.pipes, name, id);
          continue;
        }
        for (const sel of (str(prop(arg, "selector")) ?? "").split(",").map((s) => s.trim()).filter(Boolean)) push(ctx.angular.selectors, sel, id);
        if (kind !== "Component") continue;
        const members = new Set<string>();
        for (const m of n.members) if (m.name && (ts.isIdentifier(m.name) || ts.isStringLiteral(m.name))) members.add(m.name.text);
        ctx.angular.components.set(id, { id, cls: n, members, template: template(ctx, sf, arg) });
      }
    }
    ts.forEachChild(n, visit);
  };
  visit(sf);
}

function template(ctx: Context, sf: ts.SourceFile, arg: ts.ObjectLiteralExpression): AngularComponent["template"] {
  const inline = prop(arg, "template");
  if (inline && (ts.isStringLiteral(inline) || ts.isNoSubstitutionTemplateLiteral(inline)))
    return { text: inline.text, file: ctx.rel(sf.fileName), line: ctx.line(inline), external: false };
  const url = str(prop(arg, "templateUrl"));
  if (!url) return null;
  const full = path.resolve(path.dirname(sf.fileName), url);
  if (!fs.existsSync(full)) return null;
  return { text: fs.readFileSync(full, "utf8"), file: ctx.rel(full), line: 1, external: true };
}

function push(map: Map<string, string[]>, key: string, value: string) {
  const list = map.get(key) ?? [];
  if (!list.includes(value)) list.push(value);
  map.set(key, list);
}

/**
 * Second pass: parse each component's template with Angular's own parser. The template is a point
 * of its own (so a diff of the .html maps to it); the component renders its template; the template
 * renders child components (by selector), uses pipes (by name) and reads or calls component members.
 */
export function angularTemplates(ctx: Context, sf: ts.SourceFile) {
  for (const c of ctx.angular.components.values()) {
    if (c.cls.getSourceFile() !== sf || !c.template) continue;
    const t = c.template;
    const name = `${c.id.slice(c.id.indexOf("#") + 1)}.template`;
    const id = `ts|${t.file}#${name}`;
    const lines = t.text.split("\n").length;
    ctx.out.node({ id, kind: "template", name, project: ctx.o.projectId, file: t.file, line: t.line, endLine: t.line + lines - 1, isTest: false });
    ctx.out.edge(c.id, id, "renders");

    let parsed;
    try {
      parsed = parseTemplate(t.text, t.file, { preserveWhitespaces: false });
    } catch (e) {
      ctx.out.blind(id, t.file, t.line, `template could not be parsed: ${String(e).slice(0, 120)}`, "angular");
      continue;
    }
    for (const err of parsed.errors ?? []) {
      ctx.out.blind(id, t.file, t.line + (err.span?.start?.line ?? 0), `template error: ${err.msg.split("\n")[0].slice(0, 120)}`, "angular");
      break; // one is enough to say the template wasn't fully read
    }
    const seen = new Set<object>();
    const walk = (node: unknown) => {
      if (!node || typeof node !== "object" || seen.has(node as object)) return;
      seen.add(node as object);
      if (Array.isArray(node)) { node.forEach(walk); return; }
      const o = node as Record<string, unknown>;
      element(ctx, id, o);
      expression(ctx, c, id, o);
      for (const [k, v] of Object.entries(o)) if (k !== "sourceSpan" && k !== "startSourceSpan" && k !== "endSourceSpan" && k !== "keySpan" && k !== "valueSpan" && k !== "nameSpan") walk(v);
    };
    walk(parsed.nodes);
  }
}

/** An element or ng-template: link to the components and directives whose selectors match it. */
function element(ctx: Context, templateId: string, o: Record<string, unknown>) {
  const tag = typeof o.name === "string" && Array.isArray(o.attributes) && Array.isArray(o.children) ? o.name
    : typeof o.tagName === "string" && Array.isArray(o.attributes) ? o.tagName : null;
  if (!tag) return;
  const link = (key: string) => {
    const targets = ctx.angular.selectors.get(key) ?? [];
    for (const target of targets) ctx.out.edge(templateId, target, "renders", targets.length === 1 ? 0 : 1, "angular");
  };
  link(tag);
  const attrs = [o.attributes, o.inputs, o.outputs, o.templateAttrs].flatMap((a) => (Array.isArray(a) ? a : []));
  for (const a of attrs) {
    const n = (a as { name?: unknown }).name;
    if (typeof n === "string") { link(`[${n}]`); link(`${tag}[${n}]`); }
  }
}

/** Expressions: member reads and calls on the component (implicit receiver), and pipes. */
function expression(ctx: Context, c: AngularComponent, templateId: string, o: Record<string, unknown>) {
  const ctor = (o.constructor as { name?: string } | undefined)?.name ?? "";
  const receiver = o.receiver as Record<string, unknown> | undefined;
  const receiverCtor = (receiver?.constructor as { name?: string } | undefined)?.name ?? "";
  const implicit = /ImplicitReceiver|ThisReceiver/.test(receiverCtor);
  if (implicit && typeof o.name === "string" && /PropertyRead|SafePropertyRead|PropertyWrite/.test(ctor) && c.members.has(o.name)) {
    ctx.out.edge(templateId, `${c.id}.${o.name}`, "uses", 0, "angular");
  }
  if (/^Call|SafeCall/.test(ctor) && receiver && typeof receiver.name === "string" && c.members.has(receiver.name)) {
    const inner = receiver.receiver as Record<string, unknown> | undefined;
    if (/ImplicitReceiver|ThisReceiver/.test((inner?.constructor as { name?: string } | undefined)?.name ?? ""))
      ctx.out.edge(templateId, `${c.id}.${receiver.name}`, "calls", 0, "angular");
  }
  if (/BindingPipe/.test(ctor) && typeof o.name === "string") {
    const targets = ctx.angular.pipes.get(o.name) ?? [];
    for (const target of targets) ctx.out.edge(templateId, target, "uses", targets.length === 1 ? 0 : 1, "angular");
  }
}

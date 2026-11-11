import { test } from "node:test";
import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { extract } from "../src/extract.ts";
import type { Line } from "../src/model.ts";

function project(files: Record<string, string>): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "stangent-forms-"));
  for (const [name, text] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(root, name)), { recursive: true });
    fs.writeFileSync(path.join(root, name), text);
  }
  fs.writeFileSync(path.join(root, "package.json"), '{ "name": "t" }');
  fs.writeFileSync(path.join(root, "tsconfig.json"), JSON.stringify({ compilerOptions: { target: "ES2022", module: "ES2022", moduleResolution: "bundler" }, include: ["src"] }));
  return root;
}
type Fact = Extract<Line, { t: "fact" }>;

test("an Angular signal form's rules are rule facts on the field holding the form, naming the model field (D209)", () => {
  const root = project({
    "src/login.ts": [
      "import { form, required, minLength, apply, schema, validate } from '@angular/forms/signals';",
      "import { required as other } from './mine';",
      "declare function signal<T>(v: T): () => T;",
      "const shared = schema<{ city: string }>((p) => { required(p.city); });",
      "export class LoginComponent {",
      "  formModel = signal({ username: '', password: '', address: { city: '' } });",
      "  formGroup = form(this.formModel, (path) => {",
      "    required(path.username);",
      "    minLength(path.password, 6);",
      "    apply(path.address, shared);",
      "    other(path.username);",
      "    validate(path.password, (ctx) => {\n      return null;\n    });",
      "  });",
      "}",
    ].join("\n"),
    "src/mine.ts": "export function required(x: unknown) {}",
  });
  const lines = extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines;
  const rules = lines.filter((l): l is Fact => l.t === "fact" && l.kind === "rule")
    .map((f) => `${f.node.split("#")[1]} ${f.line} ${f.data.rule}(${f.data.property}${f.data.args ? ", " + f.data.args : ""}) model ${f.data.model.split("#")[1]}`);
  // `apply` reuses a schema (not a rule); `other` is the app's own function; `schema(...)` outside a form isn't read.
  assert.deepEqual(rules, [
    "LoginComponent.formGroup 8 required(username) model LoginComponent.formModel",
    "LoginComponent.formGroup 9 minLength(password, 6) model LoginComponent.formModel",
    "LoginComponent.formGroup 12 validate(password, a custom check) model LoginComponent.formModel",
  ]);
});

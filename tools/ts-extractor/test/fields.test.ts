import { test } from "node:test";
import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { extract } from "../src/extract.ts";

function project(files: Record<string, string>): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "stangent-fields-"));
  for (const [name, text] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(root, name)), { recursive: true });
    fs.writeFileSync(path.join(root, name), text);
  }
  fs.writeFileSync(path.join(root, "package.json"), '{ "name": "t" }');
  fs.writeFileSync(path.join(root, "tsconfig.json"), JSON.stringify({ compilerOptions: { target: "ES2022", module: "ES2022", moduleResolution: "bundler", jsx: "preserve", experimentalDecorators: true }, include: ["src"] }));
  return root;
}

type Fact = { kind: string; file: string; line: number; data: Record<string, string> };
function facts(root: string, kind: string): Fact[] {
  return extract({ tsconfig: path.join(root, "tsconfig.json"), root, projectId: "p", settings: {} }).lines
    .flatMap((l) => (l.t === "fact" && l.kind === kind ? [{ kind: l.kind, file: l.file as string, line: l.line as number, data: l.data as Record<string, string> }] : []));
}
/** A field as `line element bind=to via label|help choices [inside]`. */
const show = (f: Fact) => `${f.line} ${f.data.element} ${f.data.bind}=${f.data.to} ${f.data.via} ${f.data.label || f.data.labelText}|${f.data.help || f.data.helpText} ${f.data.choices}${f.data.inside ? " [" + f.data.inside + "]" : ""}`;

const ngComponent = (name: string, selector: string, template: string, body = "") => [
  "declare function Component(a: unknown): ClassDecorator;",
  "declare function input<T>(v?: T): () => T;",
  "declare function model<T>(v?: T): () => T;",
  `@Component({ selector: '${selector}', templateUrl: './${name}.html' })`,
  `export class ${name} { ${body} }`,
].join("\n") + `\n//${template.length}`;

test("Angular: a button's text key takes the template's transloco prefix; the pipe's key doesn't (D329)", () => {
  const root = project({
    "src/Card.ts": ngComponent("Card", "app-card", "", "save() {} drop() {}"),
    "src/Card.html": [
      "<ng-container *transloco=\"let t; prefix: 'device-card'\">",
      "  <button (click)=\"save()\">{{t('save')}}</button>",
      "  <button (click)=\"drop()\">{{'common.remove' | transloco}}</button>",
      "</ng-container>",
      "<button (click)=\"save()\">{{t('outside')}}</button>",
    ].join("\n"),
  });
  assert.deepEqual(facts(root, "ui-event").map((f) => f.data.keys), ["device-card.save", "common.remove", "outside"]);
});

test("Angular: fields by binding, native controls, a wrapper by what its template does; labels, help and choices", () => {
  const root = project({
    "src/kinds.ts": "export enum Kind { Book, Comic }",
    // A wrapper: its own select's value reads an input it is given. Its name says nothing.
    "src/Pick.ts": ngComponent("Pick", "app-pick", "", "value = model<number>(0); options = input<number[]>([]); inputId = input('');"),
    "src/Pick.html": "<select [id]=\"inputId()\" [value]=\"value()\"><option>one</option></select>",
    // Not a wrapper: a card whose checkbox shows a one-way input (D332: only two-way values count).
    "src/Card.ts": ngComponent("Card", "app-card", "", "selected = input(false);"),
    "src/Card.html": "<div><input type=\"checkbox\" [checked]=\"selected()\" /></div>",
    // Not a wrapper: it shows what it is given, it binds no control to it.
    "src/Row.ts": ngComponent("Row", "app-row", "", "title = input(''); subtitle = input('');"),
    "src/Row.html": "<h6>{{title()}}</h6><ng-content></ng-content><p>{{subtitle()}}</p>",
    "src/Settings.ts": [
      "import { Kind } from './kinds';",
      ngComponent("Settings", "app-settings", "", "kinds = Object.values(Kind); form: any; model: any;"),
    ].join("\n"),
    "src/Settings.html": [
      "<ng-container *transloco=\"let t; prefix: 'settings'\">",
      "  <label for=\"name\">{{t('name-label')}}</label>",
      "  <input id=\"name\" formControlName=\"name\" />",
      "  <app-row [title]=\"t('notify-label')\" [subtitle]=\"t('notify-help')\">",
      "    <input type=\"checkbox\" [(ngModel)]=\"model.notify\" />",
      "  </app-row>",
      "  <input type=\"hidden\" formControlName=\"id\" />",
      "  <select formControlName=\"size\"><option value=\"s\">{{t('small')}}</option><option value=\"l\">{{t('large')}}</option></select>",
      "  <app-pick [value]=\"model.kind\" [options]=\"kinds\" />",
      "  <textarea></textarea>",
      "  <app-card [selected]=\"true\" />",
      "</ng-container>",
    ].join("\n"),
  });
  const fields = facts(root, "form-field").map(show);
  assert.deepEqual(fields.filter((f) => !f.includes("[wrapper]")), [
    "1 input checked=selected() native | ",
    "3 input formControlName=name binding settings.name-label| ",
    "5 input ngModel=model.notify binding settings.notify-label|settings.notify-help ",
    "8 select formControlName=size binding | list:settings.small|settings.large",
    "9 app-pick value=model.kind wrapper | enum:Kind",
    "10 textarea = native | ",
  ]);
  assert.deepEqual(fields.filter((f) => f.includes("[wrapper]")), ["1 select value=value() native | list:one [wrapper]"]);
  assert.ok(!fields.some((f) => f.includes("app-card")), "a one-way card isn't a field");
});

test("React: a wrapper reached through a spread of props; name with onChange; label and help from the row around it", () => {
  const root = project({
    "src/translate.ts": "export default function translate(k: string) { return k; }",
    "src/TextBox.tsx": "export function TextBox({ value, onChange }: any) { return <input type=\"text\" value={value} onChange={onChange} />; }",
    "src/Field.tsx": "import { TextBox } from './TextBox';\nexport function Field(props: any) { return <div><TextBox {...props} /></div>; }",
    "src/Shows.tsx": "export function Shows({ items }: any) { return <ul>{items.length}</ul>; }",
    // Not a wrapper: its select shows a prop under another name (a pager's page), D332.
    "src/Pager.tsx": "export function Pager({ page, onPage }: any) { return <select value={page} onChange={onPage}><option>1</option></select>; }",
    "src/Page.tsx": [
      "import translate from './translate';",
      "import { Field } from './Field';",
      "import { Shows } from './Shows';",
      "const MODES = [ { key: 'a', value: 'Alpha' }, { key: 'b', value: 'Beta' } ];",
      "declare const Row: any, RowLabel: any, RowHelp: any, Anything: any;",
      "export function Page({ v, set }: any) {",
      "  return <form>",
      "    <Field value={v} label={translate('Name')} helpText={translate('NameHelp')} />",
      "    <Row>",
      "      <RowLabel>{translate('Mode')}</RowLabel>",
      "      <RowHelp text={translate('ModeHelp')} />",
      "      <RowHelp text={v === 'odd' ? translate('ModeOdd') : undefined} />",
      "      <Anything",
      "        name=\"mode\" values={MODES} onChange={set} />",
      "    </Row>",
      "    <Shows items={[1]} />",
      "    <Pager page={1} onPage={set} />",
      "    <input type=\"submit\" />",
      "    <select name=\"size\"><option value=\"s\">Small</option><option value=\"l\">{translate('Large')}</option></select>",
      "  </form>;",
      "}",
    ].join("\n"),
  });
  const fields = facts(root, "form-field").map(show);
  assert.deepEqual(fields, [
    "2 TextBox = wrapper |  [wrapper]",
    "8 Field value=v wrapper Name|NameHelp ",
    // The compared literal ('odd') isn't help text; the key shown is.
    "13 Anything name=mode name+onChange Mode|ModeHelp|ModeOdd list:Alpha|Beta",
    "19 select name=size native | list:Small|Large",
    // The pager's own select is a field in its file; its uses aren't (page isn't passed as value).
    "1 select value=page native | list:1",
    "1 input value=value native |  [wrapper]",
  ]);
});

test("data menus: entries with a name and a handler but no address are actions, with their submenu; two of them are a menu (D329, D332)", () => {
  const root = project({
    "src/actions.ts": [
      "enum Act { Read, Unread, More, Edit }",
      "function noop() {}",
      "export class Factory {",
      "  items = [",
      "    { action: Act.Read, title: 'mark-read', description: 'mark-read-tooltip', callback: noop, children: [] },",
      "    { action: Act.Unread, title: 'mark-unread', callback: noop, children: [] },",
      "    { action: Act.More, title: 'others', callback: noop, children: [",
      "      { action: Act.Edit, title: 'edit',",
      "        callback: noop },",
      "    ] },",
      "  ];",
      "  two = [ { label: 'A', command: noop }, { label: 'B', command: noop } ];",
      "  one = [ { label: 'Alone', command: noop } ];",
      "  linksTwo = [ { label: 'X', to: '/x' }, { label: 'Y', to: '/y' } ];",
      "  notNamed = [ { callback: noop }, { callback: noop }, { callback: noop } ];",
      "}",
    ].join("\n"),
  });
  assert.deepEqual(facts(root, "menu-item").map((f) => `${f.line} ${f.data.path} ${f.data.name} ${f.data.handler} @${f.data.handlerLine} ${f.data.help}`), [
    "5 1 mark-read callback=noop|action=Act.Read @5 mark-read-tooltip",
    "6 2 mark-unread callback=noop|action=Act.Unread @6 ",
    "7 3 others callback=noop|action=Act.More @7 ",
    "8 3.1 edit callback=noop|action=Act.Edit @9 ",
    // Two action entries are a menu (D332); one isn't, nor two links (D221's three or more).
    "12 1 A command=noop @12 ",
    "12 2 B command=noop @12 ",
  ]);
});

test("screen titles: a route's title, else under data; a constant only (D333)", () => {
  const root = project({
    "src/List.ts": ngComponent("List", "app-list", ""),
    "src/List.html": "<div></div>",
    "src/routes.ts": [
      "import { List } from './List';",
      "const name = 'title.archive';",
      "export const routes = [",
      "  { path: 'list', component: List, title: 'title.list' },",
      "  { path: 'archive', component: List, data: { title: name } },",
      "  { path: 'other', component: List, title: someResolver },",
      "  { path: 'plain', component: List },",
      "];",
      "declare const someResolver: any;",
    ].join("\n"),
  });
  assert.deepEqual(facts(root, "screen-title").map((f) => `${f.line} ${f.data.via} ${f.data.title}`), ["4 route title.list", "5 route title.archive"]);
});

test("screen titles: the outermost wrapping element's title, and the highest heading; tooltips and empty headings aren't (D333)", () => {
  const root = project({
    "src/Page.tsx": [
      "declare function translate(k: string): string;",
      "declare function PageContent(p: any): any;",
      "export function Stats() {",
      "  return (",
      "    <PageContent title={translate('Statistics')}>",
      "      <button title={translate('Refresh')}>x</button>",
      "      <h3>{translate('ByYear')}</h3>",
      "      <h2>{translate('Totals')}</h2>",
      "      <h1>{}</h1>",
      "    </PageContent>",
      "  );",
      "}",
      "export function Tip() { return <div><button title=\"Hint\">x</button></div>; }",
    ].join("\n"),
    "src/Shelf.ts": ngComponent("Shelf", "app-shelf", ""),
    "src/Shelf.html": [
      "<ng-container *transloco=\"let t; prefix: 'shelf'\">",
      "  <div class=\"bar\"><h4 title>{{name}}</h4><h5>{{t('count')}}</h5></div>",
      "</ng-container>",
    ].join("\n"),
  });
  assert.deepEqual(facts(root, "screen-title").map((f) => `${f.file}:${f.line} ${f.data.via}${f.data.level ? " h" + f.data.level : ""} ${f.data.label || f.data.labelText}`), [
    "src/Page.tsx:5 title-attribute Statistics",
    "src/Page.tsx:8 heading h2 Totals",
    "src/Shelf.html:2 heading h5 shelf.count",
  ]);
});

test("choices drawn through the field's own template are made at runtime, not the raw values (D333)", () => {
  const root = project({
    "src/Pick.ts": ngComponent("Pick", "app-pick", "", "value = model<string>(''); options = input<string[]>([]);"),
    "src/Pick.html": "<select [value]=\"value()\"></select>",
    "src/Page.ts": ngComponent("Page", "app-page", "", "styles = ['horizontal', 'vertical']; plain = ['left', 'right']; form: any;"),
    "src/Page.html": [
      "<app-pick [value]=\"form.style\" [options]=\"styles\"><ng-template #template let-x>{{ x | stylePipe }}</ng-template></app-pick>",
      "<app-pick [value]=\"form.side\" [options]=\"plain\"></app-pick>",
    ].join("\n"),
  });
  assert.deepEqual(facts(root, "form-field").filter((f) => f.file === "src/Page.html").map((f) => f.data.choices), ["runtime:styles", "list:left|right"]);
});

// The extractor's output: one JSON object per line, in the shapes the C# side reads into a
// fragment (docs/code-graph.md). Labels: 0 = ✅ certain, 1 = 🟡 guessed.

export type Line =
  | { t: "node"; id: string; kind: string; name: string; project: string; file: string | null; line: number; endLine: number; isTest: boolean }
  | { t: "edge"; source: string; target: string; kind: string; label: 0 | 1; detector: string }
  | { t: "blind"; node: string; file: string; line: number; reason: string; detector: string }
  | { t: "fact"; detector: string; kind: string; node: string; file: string | null; line: number; data: Record<string, string> }
  | { t: "enum"; id: string; name: string; file: string; line: number; endLine: number; members: { name: string; value: string | null; line: number }[] }
  | { t: "warning"; message: string };

/** N5: a `<script>` block inside a server-rendered view: its code belongs to the view, its lines to the view's file. */
export interface InlineScript { owner: string; file: string; line: number; text: string }

export interface Options {
  /** N5: plain scripts with no tsconfig: these files (absolute), read as browser scripts; `tsconfig` is then their folder. */
  files?: string[];
  /** N5: views' inline scripts, read with `files`. */
  inline?: InlineScript[];
  /** The project's tsconfig (tsconfig.app.json or tsconfig.json). */
  tsconfig: string;
  /** Repo root; every path in the output is relative to it, with forward slashes. */
  root: string;
  /** The C# side's project id for this frontend, e.g. project:src/Web/ClientApp/tsconfig.json. */
  projectId: string;
  /** detectorSettings from stangent.yaml, e.g. { "frontend-http": { wrappers: ["useApiQuery:path"] } }. */
  settings: Record<string, Record<string, string[]>>;
  /** Also print enums, members and values (C.2). Off, the output is what it always was. */
  enums?: boolean;
  /** Also a page for each screen the app's root shows with no route. Off, the output is what it always was. */
  rootScreens?: boolean;
  /** Also a page for `<Route path render|children>`, read from the JSX it shows. Off, the output is what it always was. */
  routeRender?: boolean;
}

/** Collects lines, dropping duplicate nodes and edges, in the order they were first seen. */
export class Output {
  readonly lines: Line[] = [];
  readonly nodeIds = new Set<string>();
  private readonly edgeKeys = new Set<string>();

  node(n: Omit<Extract<Line, { t: "node" }>, "t">) {
    if (this.nodeIds.has(n.id)) return;
    this.nodeIds.add(n.id);
    this.lines.push({ t: "node", ...n });
  }

  edge(source: string | null | undefined, target: string | null | undefined, kind: string, label: 0 | 1 = 0, detector = "typescript") {
    if (!source || !target || source === target) return;
    const key = `${source}\u0000${target}\u0000${kind}\u0000${detector}`;
    if (this.edgeKeys.has(key)) return;
    this.edgeKeys.add(key);
    this.lines.push({ t: "edge", source, target, kind, label, detector });
  }

  blind(node: string, file: string, line: number, reason: string, detector = "typescript") {
    this.lines.push({ t: "blind", node, file, line, reason, detector });
  }

  fact(detector: string, kind: string, node: string, file: string | null, line: number, data: Record<string, string>) {
    this.lines.push({ t: "fact", detector, kind, node, file, line, data });
  }

  enumDecl(e: Omit<Extract<Line, { t: "enum" }>, "t">) {
    this.lines.push({ t: "enum", ...e });
  }

  warning(message: string) {
    this.lines.push({ t: "warning", message });
  }
}

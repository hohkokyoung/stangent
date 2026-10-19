// The extractor's output: one JSON object per line, in the shapes the C# side reads into a
// fragment (docs/code-graph.md). Labels: 0 = ✅ certain, 1 = 🟡 guessed.

export type Line =
  | { t: "node"; id: string; kind: string; name: string; project: string; file: string | null; line: number; endLine: number; isTest: boolean }
  | { t: "edge"; source: string; target: string; kind: string; label: 0 | 1; detector: string }
  | { t: "blind"; node: string; file: string; line: number; reason: string; detector: string }
  | { t: "fact"; detector: string; kind: string; node: string; file: string | null; line: number; data: Record<string, string> }
  | { t: "warning"; message: string };

export interface Options {
  /** The project's tsconfig (tsconfig.app.json or tsconfig.json). */
  tsconfig: string;
  /** Repo root; every path in the output is relative to it, with forward slashes. */
  root: string;
  /** The C# side's project id for this frontend, e.g. project:src/Web/ClientApp/tsconfig.json. */
  projectId: string;
  /** detectorSettings from stangent.yaml, e.g. { "frontend-http": { wrappers: ["useApiQuery:path"] } }. */
  settings: Record<string, Record<string, string[]>>;
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

  warning(message: string) {
    this.lines.push({ t: "warning", message });
  }
}

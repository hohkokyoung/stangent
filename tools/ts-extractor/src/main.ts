// stangent-ts-extract: run by `stangent graph build` for each frontend project (D30).
//   node stangent-ts-extract.mjs --tsconfig <path> --root <repo> --project <id> [--settings <json>]
// Prints the project's fragment as JSON lines (docs/code-graph.md) on stdout.
import * as path from "node:path";
import { extract } from "./extract.ts";
import type { Options } from "./model.ts";

function arg(name: string): string | undefined {
  const i = process.argv.indexOf(name);
  return i >= 0 ? process.argv[i + 1] : undefined;
}

const tsconfig = arg("--tsconfig");
const root = arg("--root");
const project = arg("--project");
if (!tsconfig || !root || !project) {
  process.stderr.write("usage: stangent-ts-extract --tsconfig <path> --root <repo> --project <id> [--settings <json>]\n");
  process.exit(2);
}
// Absolute paths: the compiler reports file names the way it was given them.
const options: Options = { tsconfig: path.resolve(tsconfig), root: path.resolve(root), projectId: project, settings: JSON.parse(arg("--settings") ?? "{}") };
const out = extract(options);
const chunks: string[] = [];
for (const line of out.lines) chunks.push(JSON.stringify(line));
process.stdout.write(chunks.join("\n") + (chunks.length ? "\n" : ""));

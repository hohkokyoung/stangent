// stangent-test-steps: run by `stangent workflows tests` (D149).
//   node stangent-test-steps.mjs --root <repo> --files <a file listing specs, one path per line, relative to the repo>
// Prints one JSON line per spec. A bundle of its own, so the graph's extractor and its hash stay as they are.
import * as fs from "node:fs";
import * as path from "node:path";
import { readTestFile } from "./testSteps.ts";

function arg(name: string): string | undefined {
  const i = process.argv.indexOf(name);
  return i >= 0 ? process.argv[i + 1] : undefined;
}

const root = arg("--root");
const list = arg("--files");
if (!root || !list) {
  process.stderr.write("usage: stangent-test-steps --root <repo> --files <list>\n");
  process.exit(2);
}
const chunks: string[] = [];
for (const file of fs.readFileSync(list, "utf8").split("\n").filter((f) => f.length > 0))
  chunks.push(JSON.stringify(readTestFile(file, fs.readFileSync(path.join(root, file), "utf8"))));
process.stdout.write(chunks.join("\n") + (chunks.length ? "\n" : ""));

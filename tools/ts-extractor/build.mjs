// Bundles the extractor and its compilers (TypeScript 6, @angular/compiler) into one file, so the
// dotnet tool can ship it without an npm install on the user's machine.
import { build } from "esbuild";
import { copyFileSync, mkdirSync, readdirSync } from "node:fs";

await build({
  entryPoints: ["src/main.ts"],
  bundle: true,
  platform: "node",
  format: "esm",
  target: "node20",
  outfile: "dist/stangent-ts-extract.mjs",
  legalComments: "external",
  // TypeScript's library is CommonJS: it uses require(), __filename and __dirname.
  banner: {
    js: "import { createRequire as __cr } from 'module'; import { fileURLToPath as __fu } from 'url'; import { dirname as __dn } from 'path';"
      + " const require = __cr(import.meta.url); const __filename = __fu(import.meta.url); const __dirname = __dn(__filename);",
  },
  logLevel: "warning",
});

// TypeScript looks for its standard library (lib.es2022.d.ts, lib.dom.d.ts…) next to the file
// that runs it. Without these, Promise, Array and the DOM have no types and many links are lost.
mkdirSync("dist", { recursive: true });
for (const f of readdirSync("node_modules/typescript/lib").filter((f) => /^lib\..*\.d\.ts$/.test(f)))
  copyFileSync(`node_modules/typescript/lib/${f}`, `dist/${f}`);


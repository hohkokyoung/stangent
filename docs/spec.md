# The spec (Phase 2: contracts `reference-page`, `source-document`, `trace-links`)

"The spec" is three layers (D52), each with its own check:

| Layer | Where | Written by | Checked by |
|---|---|---|---|
| **Reference pages**: what the code does, per screen, shared component and backend entry point | `specs/reference/` | AI (Claude Code subagents, D60) | code: schema, every citation in the graph and in the page's slice; a fresh verifier (D62) |
| **The FSD**: the documents the client agreed, converted to text | `specs/fsd/<doc>/` | the client / BA; converted by code | same file → same bytes |
| **Links, findings, decisions** | `specs/trace.yaml` | AI proposes, people decide | code: quotes word for word, ids, cited symbols, `decided_by` from git |

Everything above is committed: AI output is stored, not regenerated (D18). The graph (D25) and the AI steps' working
files (`.stangent/spec/`, `.stangent/fsd/`) are not.

## How an AI step runs (D60, D61)

Every AI step is the same small loop. The CLI holds all the logic; a driver only runs the subagent each step names.

1. `stangent … prepare` writes the step's input as one file (instructions, output schema, the evidence) and prints
   `result.next`: `{action, agent, prompt, output}`, or `action: none` when nothing needs AI (inputs unchanged, D18).
2. The driver starts the named subagent from the plugin with exactly that prompt. Its tools are `Read` and `Write`.
3. `stangent … check [--usage <claude json>]` checks the output by code and prints the next step. A failed check
   gives the subagent **one retry** with the specific errors; a second failure is recorded as failed.

Drivers: `/stangent:spec-generate` and `/stangent:fsd-link` in a Claude Code session, and
`plugin/scripts/spec-run.sh` for batches and CI (headless `claude -p --output-format json`, so tokens, cost, model and
time are recorded per step; `--jobs N` runs pages side by side). The batch driver makes **one turn per call** (D64):
the step's input files go into the prompt, Claude Code runs with no tools and none of the user's settings, skills or
MCP servers (`--tools "" --setting-sources "" --disable-slash-commands --strict-mcp-config`), the output schema is
enforced with `--json-schema` (`next.schema`; `stangent spec schema <name>` prints it), and the driver writes the
structured answer to `next.output`, so `check` is unchanged and input hashes are the same in both modes. `--agents`
runs the plugin's subagents instead, each allowed to read and write only under `.stangent/`
(`--permission-mode dontAsk --allowedTools "Read(.stangent/**)" "Edit(.stangent/**)"`). Files
drivers share are written whole (temp file, then rename) under a lock. A section whose candidate pages have no recorded
version is reported as failed, never turned into a gap. `ai.enabled: false` stops every `prepare` with exit
code 4 (D24); `spec stale`, `fsd import`, `fsd candidates`, `fsd undocumented` and `trace check` are code only.

## Reference pages (2a, D54, D55)

- `stangent spec pages`: every page the graph calls for.
  - **Screens:** one per frontend route, id `page:<route>`; a server-rendered action (GET, returns `View(…)`) is a screen too.
  - **Shared components:** a component in 3 or more screens' slices gets its own page; screens link to it, not into it.
  - **Backend pages:** every endpoint (screens stop at the endpoints they call) and every event or command handler.
    Id: the entry point's graph id. Files: `specs/reference/{screens,components,backend/<project>}/<name>-<hash6>.{json,md}`.
- **The slice** (D54): the walk forward from the entry point, along the lines a change travels, to `spec.depth` lines
  (`spec.backendDepth` from an endpoint or handler). Side effects one step back: handlers of events it publishes,
  tables it maps to. A point with `spec.hubCallers` or more callers is included but not walked into. ❓ blind spots in
  the slice are appended to the page by code. Settings and defaults:
  `spec: { depth: 6, backendDepth: 4, splitAtEndpoints: true, hubCallers: 10, maxBundleChars: 260000 }`.
- **The bundle** (`stangent spec bundle <page>` prints it, without the previous version): instructions, schema, the slice's symbols with ✅/🟡, key
  lines, the source of each method and component, what it couldn't see, and the page's previous version. Its input
  hash covers everything but the previous version. Over `maxBundleChars` → listed as failed, never cut.
- **The writer** (`stangent-page-writer`) returns sections `shows`, `actions` (with the `endpoints` they call), `rules`
  and `sideEffects`; every statement cites symbol ids. Code numbers them S1…, A1…, R1…, E1….
- **The verifier** (`stangent-page-verifier`, fresh context, bundle and finished page only) flags statements the slice
  doesn't support: `contradicted` (with the contradicting line, which code checks is in the slice: at least 12
  characters and two tokens, so a fragment like `return` is no evidence; otherwise the finding is dropped) or
  `unsupported`. It gets one retry when its output is missing or breaks the schema. The writer then fixes or rejects each finding once; a "fixed" statement must
  change, a rejected `unsupported` finding needs the supporting line. Findings and outcomes are recorded on the page.
- **Recorded:** the page JSON (`schemas/reference-page.v1.json`) and its Markdown, rendered by code: the commit, the
  input hash, the slice size, the writer's and verifier's model, tokens, cost and time, and retries.
  `specs/reference/failed.json` lists pages that failed, with why; they're not retried until their inputs change
  (`--retry-failed` forces it).
- **Keeping current** (D55): `stangent spec stale [--diff base..head] [--format md]` lists pages whose input hash
  changed (with the changed files in each slice and the FSD sections linked to each), new pages and removed ones, as a
  PR comment. On merge, CI runs
  `spec-run.sh --stale` and opens a spec-only PR (see `plugin/ci/spec.yml`).

## The FSD (2b, D13, D16)

- `stangent fsd import <file.docx> [--id <doc>]` converts with the Open XML SDK, in C#: headings (Word's automatic
  numbering worked out, numbers typed into headings, or plain), numbered and bulleted lists, tables, images with their
  captions. Inline formatting is dropped, because quotes are matched against this text.
- **Section ids:** the FSD's own number (`4.2.3`), or the heading path (`Calendar / Views`) where a section has none.
  Duplicate, missing and skipped numbers are reported in `problems`, never guessed.
- The original goes to the archive set in `stangent.yaml` (`fsd: { archive: { name, path, git } }`, a folder outside
  the repo or a second git repo, which gets a commit) and is cited by archive name, path and content hash.
  A file with a known hash is imported once.

## Links, findings and decisions (2b, D53, D58, D62)

- `stangent fsd candidates`: candidate pages per section, by code: section titles ↔ routes, component names and page
  titles (case, word order and plurals normalised), field labels in tables ↔ properties in a page's slice, and the
  endpoints a candidate screen calls. A section with none goes to `stangent-candidate-finder`, which sees the page
  titles only.
- `stangent fsd prepare|check <doc> <section>`: `stangent-link-judge` (fresh context) returns links — `agree`,
  `conflict`, `gap`, `undocumented` — each quoting the FSD sentence and the page statement exactly. Code checks both
  quotes word for word, both ends exist, and every symbol the quoted statement cites is in the graph. Every `agree`
  then goes to `stangent-agree-checker`, a second fresh check: `confirmed`, or `disputed` and turned into the finding it
  raises. `specs/trace-runs.json` records each section's inputs, candidates (by code or AI), tokens and retries.
- `stangent fsd undocumented`: screens no section links to are undocumented, by code, within `fsd.scope`: the areas
  the FSD says it covers, as routes or route prefixes (`/settings` covers `/settings/tags`).
- **`specs/trace.yaml`**: one entry per link or finding; people edit only `decision` (`agree`, `code-wrong`,
  `fsd-wrong`, `defer`) in a PR. `stangent trace approve <doc> <chapter>` sets `agree` on every confirmed agree link
  under a chapter in one edit. `stangent trace check [--base <ref>] [--fix]` is the CI check: schema, ids, both quotes
  still in the FSD and the page (otherwise the decision drops back to undecided), cited symbols, and `decided_by` never
  typed. After a merge, `stangent trace attribute --base <ref> --by <who>` fills `decided_by` from the code host.
- Impact answers reach FSD sections through the links (`impact-report` v5, `docs/code-graph.md`).

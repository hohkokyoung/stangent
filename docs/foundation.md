# Foundation

The pieces every product builds on, added in the overhaul (D112, D113).

## Evidence (contract `evidence` v1)

One claim's proof, in `schemas/evidence.v1.json`, with `EvidenceRecord` and `EvidenceValidator` in Core.
It's a new contract, not a change to `artifact-base`, so nothing else had to change. Products move onto it when each is rebuilt.

| Field | Meaning |
|---|---|
| `id` | Permanent: never renumbered or reused. |
| `citation.kind` | `code`, `document`, `page`, `test` or `url`. |
| `citation.ref` | `repo/path`, or a document id. |
| `citation.lines`, `symbol`, `quote` | Optional. Where exactly. |
| `commit` | The 40-hex commit read. Required for `code` and `test`. |
| `contentHash` | SHA-256 of the document read. Required for `document`. |
| `strength` | How far it's backed (below). |
| `checkedBy` | Each `{kind: code, agent or person, name, inputsHash?}`. |

| Strength | Means |
|---|---|
| `ran` | A test or run shows it. |
| `stated` | A person or the FSD said it. |
| `read` | Read in the code only. |
| `guess` | Nothing backs it yet. |

Three rules live in the validator, not the schema:
- `lines.start` is not after `lines.end`.
- `checkedBy` may be empty only for a `guess` (D18).
- `ran` needs a checker of kind `code` or `person`. An agent saying a test passed isn't a test passing.

## Permanent ids

`new PermanentIds(path)` keeps a ledger in the file you name.

- `Issue(scope, format)` gives the next id, e.g. `R-{0:D4}` gives `R-0001`. A scope keeps its first format.
- `Remove(scope, id)` marks an id removed. It stays on the file and never comes back.
- `Active(scope)` and `Removed(scope)` list them.

Every call reads the file under a lock (`<file>.lock`, beside it) and writes it whole, so parallel drivers can't get the
same id. Commit the ledger; ignore the lock. A damaged file stops the call, because starting over would give out old ids again.

Nothing uses it yet. Requirement ids (`RequirementFile.NextId`) have the same shape: a last number per kind, so the move in
2.3 is `{"format": "R-{0:D4}", "last": <n>, "removed": […]}` per kind. That code reads and writes without a lock, so two
parallel runs can be given the same id today.

## The client-data check

`stangent data check [--terms <file>]` looks through the files `git ls-files` lists. Exit 1 on any finding.

**Tracked documents.** `.docx .doc .pdf .xlsx .xls .pptx .ppt .msg .eml` are flagged. A path is allowed only by an entry in
`.stangent-data-allow`, one per line: `<path> # <reason>`. Exact paths, and a reason is required. The file counts only when
git tracks it, so it can't pass here and fail in CI. An entry with no reason, or for a file that's gone, is a finding too.

**Client terms.** Any line holding a term from the terms file is flagged, in any case. The file is `--terms`, else
`$STANGENT_CLIENT_TERMS`, else `~/.config/stangent/client-terms.txt`. One term per line; `#` starts a comment.
- A terms file inside the repo is refused, even through a link or from another worktree of the repo.
- A file you named must exist. Only the default may be absent.
- With no terms, the result says `termsChecked: false`, and the documents part still decides.

**Nothing leaks.** Findings hold `file`, `line`, `column` and `termLine` (the term's line in the terms file), never a term or
the text around it. A path that holds a term is hidden as well: `fileIndex` is its place in `git ls-files`.

Binary files (a NUL byte in the first 8,000) aren't searched; `skippedBinary` counts them. `skippedOther` counts links,
submodules, deleted files and unreadable files.

**Known gaps.**
- It reads the current tree, not git history. Something committed and later removed is still in the history.
- It doesn't look inside documents or other binary files, so an allowed `.docx` isn't searched, and UTF-16 text counts as binary.
- A term split across two lines isn't found.
- CI has no terms file, so CI runs the documents part only.

## Coverage

`scripts/coverage.sh` runs each test project with Microsoft's coverage extension and prints line coverage per `Stangent.*`
assembly and a total. Test assemblies are left out. A line counts as covered if any test project ran it. The reports stay in
`artifacts/coverage/`, which git ignores.

There's no floor and no CI gate (D28). It's a number to watch; a floor is the owner's call, later.

`Stangent.Cli` shows as "not loaded" and isn't in the total: no test project loads it, so its commands are covered only
through the Core, Graph, Spec and Tickets code they call.

## Driver tests

The bash drivers stay in bash (D110) and are tested from xUnit (D112): `DriverRig` runs a script with fake `claude` and
`stangent` executables first on the path, and each fake logs its calls for the test to read.

**Safety.** The environment is built from nothing: `PATH` is the fake folder plus `/usr/bin:/bin`, `HOME` is a temp folder, and
`ANTHROPIC_API_KEY` isn't there. Two tests assert the fake is what a driver reaches. `benchmark/run.sh` runs from a copy, because
its real list names real repos and its results would land in this repo.

| Driver | Tested |
|---|---|
| `plugin/scripts/spec-run.sh` | Normal path, retry, `--retry-failed`, `--all`, `--jobs`, `--stale`, `--fsd`, `--req`, `--only`, `--agents`, no AI call when nothing is needed, Claude failing, stangent crashing, a failed list, usage |
| `plugin/scripts/ticket-run.sh` | Normal path, `--no-pull`, `--refind`, `--no-find`, the file finder's worktree, `--prove`, a capped reproducer, `--match`, `--questions`, `--resolution`, `--jobs`, a failed pull or bundle, Claude failing, a blocked setup, an app that won't start, usage |
| `benchmark/run.sh` | Normal path, `BENCH_ONLY`, a repo's config, a failing command recorded, a failing init, usage |
| `benchmark/tickets/run.sh` | `points`, `files` resuming, `pages`, `bundles`, `drafts` (also in parallel), `prove`, `replay`, `--only`, a failed step, a missing Jira key, a bad phase, usage |

**Not tested.**
- `benchmark/sonarr-run-batch.sh`: a one-off with hard-coded paths to a scratch folder that no longer exists.
- `benchmark/tickets/sonarr/app/{build,setup,start}.sh`: a few commands against Sonarr, proved by the benchmark's own runs.

**Known gap.** A page or step stangent refuses (an error envelope) comes out as a status line holding its code, and the batch
exits 0. Tests pin this. A crash, a failed Claude call, a failed pull and a failed bundle all exit nonzero.

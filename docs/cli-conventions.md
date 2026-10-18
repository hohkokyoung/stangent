# CLI conventions (contract `cli-conventions` v1)

Every product talks to the store through the `stangent` CLI (decisions.md D23). These rules
are what callers (other products, Claude Code commands and hooks, CI) can rely on.

## Output

- **One JSON envelope on stdout**, compact by default (cheap for agents to read); `--pretty` indents it.
  - Success: `{"ok":true,"result":…}`
  - Failure: `{"ok":false,"error":{"code":"…","message":"…","details":[…]}}`
  - A check that ran and found problems (e.g. `phases check`) also carries its report in `result`.
- Keys are sorted, so the same result always prints the same bytes.
- Warnings go to stderr; stdout never holds anything but the envelope.
- `error.code` is stable snake_case and safe to branch on; `message` is for people and may change.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | ok |
| 1 | a check failed: invalid artifact, broken store, stale phases, missing link target |
| 2 | usage: bad arguments, no `stangent.yaml` found, bad reference syntax |
| 3 | not found |
| 4 | AI is switched off for this repo (`ai.enabled: false`, D24) |

## Input

- Artifacts and batches are JSON on stdin, or `--file <path>`.
- References are `type/id`.
- `--repo <path>` picks the repo; by default the nearest `stangent.yaml` at or above the current folder.

## Batching

`stangent store batch` takes an array of `get` / `put` / `link` / `query` ops and runs them in
one process. The whole batch is checked first, in order — an op may use an artifact created by
an earlier op, not a later one — and nothing is written if any op would fail.

## Telemetry

Every command writes one event (contract `telemetry-event` v1, D21) to
`.stangent/telemetry/<date>.jsonl` in the repo, or to `$STANGENT_TELEMETRY_DIR` when set, or to
`~/.stangent/telemetry` outside a repo. Events carry counts, codes, ids and hashes only; one
that breaks the contract is dropped with a warning, never written.

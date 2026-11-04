# CLI conventions (contract `cli-conventions` v3)

Every product talks to the store through the `stangent` CLI (decisions.md D23). These rules
are what callers (other products, Claude Code commands and hooks, CI) can rely on.

## Output

- **One JSON envelope on stdout**, compact by default (cheap for agents to read); `--pretty` indents it.
  - Success: `{"ok":true,"result":…}`. A command that uses a switchable component adds `"components":{"on":[…],"off":[…]}` beside `result` (D121), so a result's own shape never moves.
  - Failure: `{"ok":false,"error":{"code":"…","message":"…","details":[…]}}`
  - A check that ran and found problems (e.g. `phases check`) also carries its report in `result`.
- Keys are sorted, so the same result always prints the same bytes.
- Warnings go to stderr; stdout never holds anything but the envelope.
- `error.code` is stable snake_case and safe to branch on; `message` is for people and may change.
- One exception: commands with `--format md` (e.g. `graph impact`) print the Markdown itself on
  success, for PRs and people. Failures still print the JSON envelope, and exit codes still apply.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | ok |
| 1 | a check failed: invalid artifact, broken store, stale phases, missing link target |
| 2 | usage: bad arguments, no `stangent.yaml` found, bad reference syntax |
| 3 | not found |
| 4 | AI is switched off for this repo (`ai.enabled: false`, D24) |
| 5 | drivers only: stopped by `--max-cost` or a usage limit; the same command resumes (T.2, D126) |

The drivers in `plugin/scripts/` add to these: 1 means a step was refused or failed and the rest went on (I.10), 2 also means no cost cap was chosen, and 125 means the batch stopped on anything else, such as Claude Code failing.

## Input

- Artifacts and batches are JSON on stdin, or `--file <path>`.
- References are `type/id`.
- `--repo <path>` picks the repo; by default the nearest `stangent.yaml` at or above the current folder.

## Batching

`stangent store batch` takes an array of `get` / `put` / `link` / `query` ops and runs them in
one process. The whole batch is checked first, in order — an op may use an artifact created by
an earlier op, not a later one — and nothing is written if any op would fail.

## Components

`stangent components` lists every component (D121): `name`, `label`, `on`, `default`, `setting`, `switch`, `needs`, `neededBy`, `connected`, and `waitingFor` or `blocked` where they apply; then `notConnected` and `noSwitch`. `docs/foundation.md` has the list.

## Telemetry

Every command writes one event (contract `telemetry-event` v2, D21) to
`.stangent/telemetry/<date>.jsonl` in the repo, or to `$STANGENT_TELEMETRY_DIR` when set, or to
`~/.stangent/telemetry` outside a repo. An event from a command that uses a component names those on and off (`stangent.components.on`, `.off`). Events carry counts, codes, ids, hashes and names from a closed list only; one
that breaks the contract is dropped with a warning, never written. `gen_ai.usage.cost_usd` is the cost Claude Code reported for a step; `stangent cost estimate` reads it back.

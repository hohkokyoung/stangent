# The frozen flow key (D177, D183)

Layer 3's bar is scored against these files. A fresh agent wrote them from each repo's source alone,
before any flow code existed, so the build can't bend the bar. They are frozen: change one only to write
down a ruling (below), never to fit what Stangent built.

One file per flow: `<repo>/<flow-id>.json`. 15 flows: Sonarr, Kavita and nopCommerce, 5 each
(`phases/r3-flows.md`, "The bar").

## Every item rests on a code line

Every item that counts carries `at` and `code`:

- `at` — `path/from/repo/root.ext:line`, 1-based, in the commit named at the top of the file.
- `code` — the code quoted exactly. Whitespace runs count as one space, so a quote may run over several
  lines, but it must **start on line `at`** and end within the next 10 lines.

An item without both, or whose `code` isn't found there, doesn't count (D177 item 2). The scorer matches
Stangent's flow to the key by `at` first, then by `code`.

## The file

```json
{
  "repo": "sonarr",
  "commit": "<full sha of the clone>",
  "id": "add-series",
  "name": "Add series",
  "kind": "click | screen-opens | job",
  "start":  { "what": "the Add button's handler", "at": "frontend/src/….tsx:88", "code": "onPress={handleAddSeriesPress}" },
  "actor":  { "who": "signed-in user", "why": "no [AllowAnonymous] on the endpoint", "at": "…", "code": "…" },
  "checks": [ { "what": "series not already added", "at": "…", "code": "SetValidator(…)" } ],
  "steps": [
    { "n": 1, "from": null, "what": "posts the series", "at": "…", "code": "…",
      "conditions": [ { "kind": "if", "at": "…", "code": "…" } ] },
    { "n": 2, "from": 1, "what": "saves Series", "at": "…", "code": "…",
      "end": { "kind": "database", "detail": "Series" } }
  ],
  "exits": [
    { "what": "400 when …", "at": "…", "code": "return BadRequest(…)",
      "conditions": [ { "kind": "if", "at": "…", "code": "…" } ] }
  ],
  "links": [
    { "kind": "runs-later", "to": "Refresh series", "at": "…", "code": "_commandQueueManager.Push(new RefreshSeriesCommand(…))" }
  ],
  "notes": [ "what was not followed, and why" ]
}
```

- **steps** — a tree, walked forward from the start. `from` is the step it comes from (`null` for the
  first); steps under one `from` are in source-line order. A step is a real call: interface →
  implementation, DI and route hops are left out (D172).
- **end** — on the step where a branch stops (D172): `database` (the table), `outside-call`, `message`
  (the message's name; its handlers are links), `screen` (the screen it moves to), `hub` (10 or more
  callers; `detail` gives the count), `unknown` (why), `depth` (8 real calls; what was left unwalked).
- **conditions** — on a step or exit, innermost first, each quoted exactly (D174). `kind` is `if`,
  `else`, `unless` (a guard earlier in the block that returns or throws, D182), `case`, `catch`, `loop`
  or `ternary`. For `else`, `code` quotes the `if` it belongs to.
- **exits** — early exits (D174, D176 item 4): a returned `BadRequest`, `NotFound`, `Unauthorized`,
  `Forbid`, `Challenge`, `Redirect…`, `StatusCode(4xx/5xx)`, or a `throw`, with the conditions on the way.
- **links** — every place the flow leaves its bounds (D173): `message` (one per handler, `to` names it),
  `runs-later` (queued work → the job), `screen` (that screen's opening flow), `continues` (a hub or the
  depth limit). `at`/`code` is the line that sends, queues or navigates. A link may carry `conditions`
  when what it decides acts in the linked flow (Sonarr's delete options act in the event handlers).
- **checks** — validation rules that run on the input (D174, D179).
- **actor** — who can start it (D179); `at`/`code` is the access rule it rests on, if any.
- **notes** — free text; doesn't count.

## When Stangent and the key disagree

The quoted code line decides (D177). Each ruling is written in `phases/r3-flows.md` under "The bar", and
if the key was wrong, the item is fixed here in the same commit, naming the ruling.

# flutter-skill MCP tool surface

The server exposes a large tool set (the project advertises ~253 across all
platforms). Only the ones below are needed for the workflow in `SKILL.md`; reach
for the rest only when the flow genuinely requires it.

## Inspection — always before interaction

| Tool | Use |
|---|---|
| `screenshot` | Capture the current screen. Evidence, and your own orientation. |
| `inspect_interactive` | The important one. Lists every tappable/typeable element with a **semantic ref** (`button:Login`, `input:Email`). Every ref you pass to an action must have come from here. |
| `get_elements` | Full element list, including non-interactive ones. Use when `inspect_interactive` does not show what you expected. |
| `find_element` | Locate one element by key or text. |
| `get_element_properties` | Read an element's attributes — enabled state, value, semantics label. |
| `wait_for_element` | Block until an element appears. **This is the assertion primitive** — use it instead of any sleep. |

## Interaction

| Tool | Use |
|---|---|
| `tap(ref)` | Tap by semantic ref. |
| `enter_text(ref, text)` | Focus and type. |
| `set_text` / `clear_text` / `get_text` | Direct field manipulation and readback. |
| `scroll` / `scroll_to` | Bring an off-screen element into view before tapping it. |
| `swipe` / `drag` / `long_press` | Gesture input. |
| `press_key` | Hardware/soft key. |
| `go_back` | Platform back. |

## Diagnostics

| Tool | Use |
|---|---|
| `get_logs` / `clear_logs` | App logs. Call `clear_logs` before a step and `get_logs` after to attribute output to that step. |

## Assertion and audit families

`assert_text`, `assert_visible`, `assert_gone` mirror the CLI shortcuts.
`accessibility_audit`, `a11y_tab_order`, `a11y_color_contrast` back a11y checks;
`perf_start` / `perf_stop` / `perf_report` back performance ones. Both families
produce findings, not pass/fail — if you register a case from them, the `expect`
must name a concrete threshold, not "no issues".

## CLI equivalents

Every action has a CLI shortcut, which is what makes unattended runs possible:

```bash
flutter_skill launch . --detach          # start + attach, no TTY needed
flutter_skill connect --id myapp         # attach to an already-running app
flutter_skill server list                # what is attached
flutter_skill inspect --server myapp
flutter_skill tap --server myapp "button:Login"
flutter_skill screenshot --server myapp --output shot.png
flutter_skill server stop --id myapp
```

JSON output turns on automatically when `CI=true` or `GITHUB_ACTIONS=true`.

## The gap this skill exists to close

There is **no `flutter_skill run <file>`**. The CLI drives an app; it does not
replay a saved flow, and nothing in the tool set persists a session as something
re-runnable. That is why `SKILL.md` requires every verified flow to be written
out as a Flutter `integration_test` — `flutter test` is the replay mechanism,
and it is the only part of this that survives without an agent in the loop.

## Binary name

`dart pub global activate flutter_skill` installs `flutter_skill`;
`npm install -g flutter-skill` installs `flutter-skill`. Probe before use:

```bash
BIN=$(command -v flutter_skill || command -v flutter-skill)
```

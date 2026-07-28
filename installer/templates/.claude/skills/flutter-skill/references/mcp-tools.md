# flutter-skill MCP tool surface

**Every name here was read from a live `tools/list` against flutter-skill
0.9.36 connected to a running Flutter app on an iOS simulator.** That matters:
the project's own `docs/cli-server-commands.md` documents subcommands
(`connect`, `server list`, `ping`) that the shipped CLI does not have, and its
tool tables list names (`get_elements`, `find_element`, `get_text`,
`assert_gone`, `accessibility_audit`, `perf_start`) that the server does not
expose. Prefer a live `tools/list` over any document, including this one.

## The tool list is in two halves

Before an app is attached the server exposes **13 tools, all connection
management**. The ~117 interaction tools do not exist yet — the server declares
`listChanged` and grows the list once connected. Planning a flow against tools
you have not seen listed is how a run dies at its third step.

**Connection tools (always available):**

| Tool | Use |
|---|---|
| `launch_app` | Build, install and attach. `project_path` (the dir holding `pubspec.yaml`), `device_id`, `flavor`, `target`, `dart_defines`, `extra_args`. |
| `scan_and_connect` | Attach to an app that is *already* running by scanning a port range: `port_start`, `port_end`, `project_path`. |
| `connect_app` | Attach to a known VM Service URI. Must be `ws://`, not `http://`. |
| `get_connection_status` | Confirm the attach before doing anything else. |
| `list_running_apps`, `list_sessions`, `switch_session`, `disconnect` | Session management. |
| `native_list_simulators` | Enumerate simulators. |
| `diagnose_project` | Why a project will not attach. |

`launch_app` is marked the priority tool for UI testing, but on a project
without the flutter-skill bridge it can find the VM Service and still fail to
connect (`E303`, an `http://` URI where `ws://` was required). **When
`launch_app` fails that way, the app is usually running anyway** — run
`scan_and_connect` over a wide range (`49000`–`65000`) and it attaches. That
recovery is worth trying before reporting a blocker.

## Inspection — always before interaction

| Tool | Use |
|---|---|
| `inspect_interactive` | The important one. Interactive elements with the refs every action takes. |
| `inspect` | Broader inspection when `inspect_interactive` omits something. |
| `get_interactable_elements` | Explicit list of what can be acted on. |
| `get_widget_tree` | Full widget tree. |
| `get_widget_properties` | One widget's properties (**not** `get_element_properties`). |
| `find_by_type` | Locate by widget type. |
| `get_current_route`, `get_navigation_stack` | Where you actually are. Better than inferring from visible text. |
| `page_summary` | Condensed description of the screen. |
| `snapshot` | Structured state capture. |
| `get_text_content`, `get_text_value` | Read text (**not** `get_text`). |
| `wait_for_element`, `wait_for_gone`, `wait_for_idle` | The assertion primitives. Use these instead of any sleep. |

## Interaction

| Tool | Use |
|---|---|
| `tap`, `tap_at`, `double_tap` | Tap by ref, or by coordinate. |
| `enter_text`, `type_text`, `fill` | Text entry. |
| `long_press`, `long_press_at` | Press and hold. |
| `swipe`, `swipe_coordinates`, `edge_swipe`, `drag`, `gesture` | Gestures. |
| `scroll_to`, `scroll_until_visible` | Bring an element into view. There is no bare `scroll`. |
| `press_key`, `go_back`, `hover`, `focus`, `blur` | Keys, navigation, focus. |
| `select_option`, `set_checkbox`, `get_checkbox_state`, `get_slider_value` | Form controls. |
| `hot_reload`, `hot_restart`, `stop_app` | App lifecycle. `hot_restart` is the reliable way back to a clean state. |

## Assertions

`assert_text`, `assert_visible`, `assert_not_visible` (**not** `assert_gone`),
`assert_element_count`, `assert_batch`.

## Evidence and diagnostics

`screenshot`, `screenshot_element`, `screenshot_region`, `video_start` /
`video_stop`, `record_start` / `record_stop` / `record_export`.
`get_logs` / `clear_logs` — clear before a step and read after, so output is
attributable to that step. `get_errors`, `diagnose`, `get_page_state`.

## Beyond the core loop

Present and usable, but do not reach for them unless the task asks:
`explore_actions` / `explore_report` / `boundary_test` (AI exploration),
`visual_diff` / `visual_verify` / `diff_baseline_create` / `diff_compare`
(visual regression), `get_performance` / `get_frame_stats` / `get_memory_stats`,
`enable_network_monitoring` / `clear_network_requests`, `fixture_load` /
`fixture_reset` / `fixture_switch_user` / `fixture_switch_env`, the `auth_*`
family, and the `native_*` family for OS-level interaction outside the Flutter
tree.

If you register a case from a performance or visual tool, `expect` must name a
concrete threshold or baseline — those tools return findings, not pass/fail, and
"no issues" is not an assertion the next run can check.

## CLI

The CLI is much smaller than the docs suggest. Verified subcommands:

```
init  quickstart  demo  launch  server  inspect  act  screenshot  serve
doctor  setup  report-error  --version
```

There is **no** `connect`, `server list`, `ping` or `servers`.

```bash
flutter-skill doctor          # SDK, devices, ports, and whether the project is set up
flutter-skill init <app-dir>  # writes .flutter-skill.yaml, installs the bridge
```

`doctor` is the right preflight: it reports the Dart SDK, booted simulators,
bridge ports, and flags a missing `.flutter-skill.yaml`.

## The gap this skill exists to close

There is **no `flutter-skill run <file>`**. The CLI drives an app; it does not
replay a saved flow, and nothing in the tool set persists a session as something
re-runnable. That is why `SKILL.md` requires every verified flow to be written
out as a Flutter `integration_test` — `flutter test` is the replay mechanism,
and the only part that survives without an agent in the loop.

## Binary name

`dart pub global activate flutter_skill` installs `flutter_skill`;
`npm install -g flutter-skill` installs `flutter-skill`. Probe before use:

```bash
BIN=$(command -v flutter_skill || command -v flutter-skill)
```

On an fvm-managed Flutter, the pub shim calls `dart` directly and `dart` is not
on `PATH`. Both the fvm SDK bin and `~/.pub-cache/bin` must be on `PATH` for the
binary — and therefore the MCP server — to start at all.

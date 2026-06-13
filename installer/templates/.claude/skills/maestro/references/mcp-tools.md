# Maestro MCP tools

The Maestro MCP server exposes mobile automation as MCP tools. All tools are called via `mcp__maestro__<tool>`.

## Device management

### `list_devices`
```
list_devices()
```
Returns available emulators/simulators/physical devices. Always call first, then filter the result
to the target platform (`ios` or `android`) from `maestro.platforms` in `.agentic.yml` (or the
task frontmatter `platforms:` field if set). Never pick a device by position — match by platform.

### `start_device`
```
start_device(deviceId: string)
```
Starts an emulator or simulator by ID (from `list_devices`). Wait for it to be ready before launching the app.

---

## App control

### `launch_app`
```
launch_app(appId: string)
```
Launches the app by bundle ID (e.g. `com.example.myapp`). Get the appId from the task file or project config (`pubspec.yaml`, `build.gradle`, `Info.plist`).

### `stop_app`
```
stop_app(appId: string)
```
Stops the app. Use between test flows to ensure a clean state.

---

## Inspection

### `inspect_view_hierarchy`
```
inspect_view_hierarchy()
```
Returns the accessibility tree of the current screen. **Always call before tapping** to discover element labels and confirm the right screen is shown.

### `take_screenshot`
```
take_screenshot(filename?: string)
```
Takes a screenshot of the current device screen. Call after every meaningful state change. Returns the file path — record it in `## Test results`.

---

## Interaction

### `tap_on`
```
tap_on(element: string)
```
Taps an element by its visible text or accessibility label. Get the exact label from `inspect_view_hierarchy` output.

### `input_text`
```
input_text(text: string)
```
Types text into the focused input field. Always `tap_on` the input first to focus it.

### `back`
```
back()
```
Presses the Android back button / iOS back navigation.

---

## Flow execution

### `run_flow`
```
run_flow(yaml: string)
```
Runs a Maestro flow defined as an inline YAML string.

### `run_flow_files`
```
run_flow_files(paths: string[])
```
Runs one or more flow YAML files by path.

### `check_flow_syntax`
```
check_flow_syntax(yaml: string)
```
Validates a flow YAML string without running it. Always call before `run_flow` or writing the final `.yaml` file.

---

## Documentation

### `query_docs`
```
query_docs(query: string)
```
Searches Maestro documentation. Use when unsure about a command or YAML syntax.

---

## Workflow pattern

```
1. list_devices()                   → confirm device available
2. launch_app(appId)                → start the app
3. inspect_view_hierarchy()         → discover elements on current screen
4. tap_on("element label")          → navigate / interact
5. input_text("value")              → fill inputs (after tapping to focus)
6. take_screenshot("step_name")     → capture state
7. inspect_view_hierarchy()         → verify new screen
8. repeat 3-7 for each step
9. check_flow_syntax(yaml)          → validate generated YAML
10. run_flow_files([path])          → execute and confirm pass
```

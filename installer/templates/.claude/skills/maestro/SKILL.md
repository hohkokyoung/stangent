# SKILL: maestro

## Purpose
Governs how the tester runs mobile UI tests using the **Maestro MCP server**. Scope: native mobile UI verification for Flutter/React Native/iOS/Android projects. Does NOT cover unit tests, widget tests, or browser-based flows.

## HARD GATE — enforce before any other step

**PROHIBITED — you may not do any of the following until device exploration is complete:**
- Call `Write` or `Edit` to create or modify a flow YAML file
- Generate flow YAML content from the task description or your own knowledge
- Call `run_flow` or `run_flow_files` on a YAML you have not derived from live inspection

**REQUIRED order — no deviation:**
1. `mcp__maestro__list_devices` — confirm a device is available
2. `mcp__maestro__launch_app(appId)` — start the app on the live device
3. `mcp__maestro__inspect_view_hierarchy` — read the actual screen state
4. `mcp__maestro__tap_on` / `input_text` — interact with real elements by their visible labels
5. `mcp__maestro__take_screenshot` — capture evidence of each state
6. Repeat 3–5 until the full flow is covered
7. `mcp__maestro__check_flow_syntax` — validate the YAML before writing
8. **Only then:** write the flow YAML files using what you actually observed
9. `mcp__maestro__run_flow_files` — execute and confirm pass

If you are about to call `Write` for a YAML file and have not yet called `launch_app` — stop. Call `list_devices` and `launch_app` first.

Generating a flow YAML from the task description alone is a **protocol violation**. The flow must reflect what the live app actually renders, not what the task says it should render.

---

## Rules

1. **MCP-first, artifact-second.** The hard gate above is non-negotiable.
2. **One retrieve() call.** Already handled by the tester role — do not call it again.
3. **Screenshot on every meaningful state transition.** Call `take_screenshot` after each action that changes visible state. Attach paths to `## Test results`.
4. **Inspect before tapping.** Always call `inspect_view_hierarchy` before `tap_on` to confirm the element exists and get the exact visible label.
5. **Artifact into `.maestro/` at project root.** Use path `.maestro/<feature>/<task_id>_<case>.yaml`. Create the directory if it doesn't exist.
6. **Happy path → boundary → failure.** Cover all three. Each is a **separate flow YAML file**.
7. **`appId` from the task file only.** Never hardcode a bundle ID you didn't get from the task or project config (`pubspec.yaml`, `build.gradle`, `Info.plist`).
8. **Maestro Viewer is always on.** Do not suppress output or run in silent mode — the user watches the device in their browser.

## Patterns

### MCP exploration loop
```
list_devices()                   → confirm device ready
→ launch_app(appId)              → start app
→ inspect_view_hierarchy()       → read current screen labels
→ tap_on("visible label")        → navigate / interact
→ input_text("value")            → type (after tap to focus)
→ take_screenshot("step_name")   → capture state
→ inspect_view_hierarchy()       → verify new screen
→ repeat until flow complete
→ check_flow_syntax(yaml)        → validate before writing
→ THEN write flow YAML files
→ run_flow_files([paths])        → confirm pass
```

### Generated flow YAML shape
```yaml
appId: com.example.myapp
---
- launchApp
- assertVisible: "Welcome"
- tapOn: "Sign In"
- inputText: "user@example.com"
- tapOn: "Password"
- inputText: "secret"
- tapOn: "Login"
- assertVisible: "Dashboard"
- takeScreenshot: login_success
```

### File naming
```
.maestro/
  auth/
    t3_happy.yaml
    t3_boundary_invalid_email.yaml
    t3_failure_wrong_password.yaml
```

## Anti-patterns

- Writing flow YAML before `launch_app` — protocol violation, see HARD GATE.
- Generating `tapOn` labels from source code — Maestro taps by visible text/accessibility label, not code IDs. Use `inspect_view_hierarchy` output only.
- One YAML covering all cases — split happy/boundary/failure into separate files.
- Skipping `take_screenshot` — visual evidence is required in `## Test results`.
- Running `run_flow` without `check_flow_syntax` first — always validate before executing.
- Running without confirming a device with `list_devices` first.

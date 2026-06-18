# Screenshot — Mobile (Maestro) Capture Procedure

Used by `/agentic-screenshot` when `test_framework = maestro`. Execute this after Steps 1–4 of the main command. The device is confirmed live from Step 2.

---

## Capture (one screen at a time)

1. Read `appId` from `project.yml`, or from the task file, or ask if neither is available.
2. For each screen, in order:
   a. **Reset to clean state.** Call `mcp__maestro__stop_app(appId)` then `mcp__maestro__launch_app(appId)`. This guarantees a fresh start regardless of whether the screen is the default landing screen or requires navigation, and prevents prior interaction (including scroll steps) from polluting the next capture.
   b. Call `mcp__maestro__inspect_view_hierarchy()` to discover current screen state and element labels before tapping anything.
   c. Navigate to the target screen by tapping the minimal path from the current state. After each tap, call `inspect_view_hierarchy()` to confirm the navigation reached the expected intermediate or target screen before tapping further. If the screen is the default landing screen (already active after launch), skip navigation taps entirely.
   d. **Verify the correct screen is active.** Call `mcp__maestro__inspect_view_hierarchy()` one final time and confirm the hierarchy contains elements consistent with the target screen (a heading, distinctive label, or screen title). If it doesn't match — or still shows a previous or login/error screen — log as failed and continue. Do NOT screenshot a mismatched screen.
   e. **Scroll only if safe.** Examine the hierarchy from step (d). If it shows a card-based, swipeable, or pager layout (ViewPager, HorizontalScrollView, or a card filling the screen with a nested ScrollView), **skip scroll entirely** — generic scrolls target the inner panel and corrupt the capture state. Only scroll for plain vertical lists or feeds with a single top-level scrollable. If safe, call `mcp__maestro__run_flow`:
      ```yaml
      appId: <appId>
      ---
      - scroll
      - scroll
      ```
      Then scroll back to top:
      ```yaml
      appId: <appId>
      ---
      - scrollUntilVisible:
          element:
            index: 0
      ```
      If `run_flow` errors, skip the scroll step — do not abort the capture.
   f. Create the screen subfolder: `mkdir -p docs/screenshots/<timestamp>/<slug>/`
   g. `mcp__maestro__take_screenshot(filename="<slug>")` — record the file path returned.
   h. Move the file to `docs/screenshots/<timestamp>/<slug>/screen.png`

If any Maestro call fails for a screen, print `✗  <index>-<slug> — failed: <error>` and continue.

---

## Index file

Write `docs/screenshots/<timestamp>/README.md`.

```markdown
# Screenshots — <project name>

Captured: <ISO 8601 timestamp>
Framework: maestro
Device: <device name from list_devices>

## Screens

| # | Screen | Screenshot |
|---|--------|------------|
| 1 | Home | ![](home/screen.png) |
| 2 | Login | ![](login/screen.png) |
```

Omit any screen that failed.

---

## Report

```
Screenshots saved to docs/screenshots/<timestamp>/

✓  home/screen.png
✓  login/screen.png
✗  dashboard — failed: hierarchy mismatch (not captured)

Index: docs/screenshots/<timestamp>/README.md
```

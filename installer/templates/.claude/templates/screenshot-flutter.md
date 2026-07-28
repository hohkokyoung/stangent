# Screenshot — Flutter (flutter-skill) Capture Procedure

Used by `/agentic-screenshot` when `test_framework = flutter-skill`. Execute this
after Steps 1–4 of the main command. The app is confirmed attached from Step 2.

The shape mirrors the Maestro procedure, with one real difference:
`inspect_interactive` reads Flutter's semantics tree, so a screen is confirmed by
the widget keys and semantic labels it actually exposes rather than by a
rendered-text match. Prefer a `Key` over a visible string wherever the app
provides one — copy changes, keys do not.

---

## Capture (one screen at a time)

1. Confirm the attached app with `flutter_skill server list`, or
   `mcp__flutter-skill__screenshot` if you only need to prove liveness.
2. For each screen, in order:
   a. **Reset to a clean state.** `mcp__flutter-skill__go_back` until the root is
      reached, or hot-restart with `flutter_skill hot_restart`. Restart is the
      reliable option — a screen reached by backing out of a half-filled form is
      not the same screen a user sees on a fresh launch, and the difference shows
      up in the capture.
   b. `mcp__flutter-skill__inspect_interactive()` — read the current screen's
      real elements before touching anything.
   c. Navigate by tapping the minimal path, `tap(ref: ...)` using refs from (b)
      only. Re-inspect after each tap to confirm you landed where you expected
      before tapping again.
   d. **Verify the target screen is active.** Call
      `mcp__flutter-skill__wait_for_element(key: "<screen key>")`. If it times
      out, log the screen as failed and continue — do NOT screenshot a screen you
      could not confirm. A mislabelled screenshot is worse than a missing one: it
      is wrong in a document people trust.
   e. **Scroll only if safe.** Check the inspection from (d). Skip scrolling on
      `PageView`, `TabBarView`, or a card that fills the screen with a nested
      scrollable — a generic scroll targets the inner view and corrupts the
      capture. For a plain vertical list, `scroll_to` the last item and then back
      to the first.
   f. Create the screen subfolder:
      `mkdir -p docs/screenshots/<timestamp>/<slug>/`
   g. `mcp__flutter-skill__screenshot()` — record the returned path.
   h. Move the file to `docs/screenshots/<timestamp>/<slug>/screen.png`

If any flutter-skill call fails for a screen, print
`✗  <index>-<slug> — failed: <error>` and continue.

---

## Resolving screen names

Flutter has no route-file convention to scan the way Next.js does. In order:

1. A `GoRouter` / `Navigator` route table — grep for `GoRoute(`, `routes:`,
   `onGenerateRoute`, or a `Routes` constants class.
2. `MaterialApp(routes: {...})` named-route map.
3. If neither exists, ask the developer to list screens. Do not guess from widget
   class names — a `LoginPage` class may not be a reachable screen, and capturing
   an unreachable one produces a confident-looking screenshot of nothing.

---

## Index file

Write `docs/screenshots/<timestamp>/README.md`.

```markdown
# Screenshots — <project name>

Captured: <ISO 8601 timestamp>
Framework: flutter-skill
Device: <device id used, e.g. ios-17-iphone15 / chrome>

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
✗  dashboard — failed: wait_for_element timed out (not captured)

2 captured, 1 failed
```

State the failed count explicitly. A report that lists only successes reads as a
complete set, and the next person to open the folder has no way to tell that a
screen is missing rather than absent from the app.

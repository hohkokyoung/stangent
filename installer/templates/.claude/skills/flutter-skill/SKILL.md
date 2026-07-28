# SKILL: flutter-skill

## Purpose
Governs how the tester verifies **Flutter** app behaviour using the
**flutter-skill MCP server** (`ai-dashboad/flutter-skill`), and how each verified
flow is turned into a re-runnable `integration_test`. Scope: end-to-end UI
verification of a Flutter app on a simulator, emulator or web target. Pairs with
the `regression` skill, which owns registering the result; this skill owns
execution only. Does NOT cover unit tests, widget tests, non-Flutter mobile
(use `maestro`), or browser flows against a non-Flutter frontend (use
`playwright`).

## HARD GATE — enforce before any other step

**PROHIBITED until the app is attached and inspected:**
- Calling `Write` or `Edit` to create an `integration_test/*_test.dart` file
- Writing finders, keys or labels taken from source code rather than from a live
  `inspect_interactive` result
- Registering a case in `.claude/tests/` for a flow you have not driven

**REQUIRED order — no deviation:**
1. **Attach through MCP, not the CLI.** Call `mcp__flutter-skill__launch_app`
   with `project_path` set to the directory holding `pubspec.yaml` (in a
   monorepo that is the app subdirectory, e.g. `mobile/`, **not** the repo
   root), plus `device_id` when the task names a device. Use
   `mcp__flutter-skill__scan_and_connect` instead when the app is already
   running. Confirm with `mcp__flutter-skill__get_connection_status`.

   **If `launch_app` fails with `E303` ("found VM Service but failed to
   connect"), do not stop — the app is usually running anyway.** Call
   `scan_and_connect` with `port_start: 49000, port_end: 65000`; it attaches
   where `launch_app` did not. Only after that also fails, set `status: blocked`
   with `blocker: "no_flutter_app_attached: <what both reported>"`.
2. **Re-read the tool list.** Until an app is attached the server exposes only
   13 connection-management tools; the ~117 inspection and interaction tools
   appear after step 1 (it declares `listChanged`). Do not plan a flow around a
   tool you have not seen in the post-connection list — and do not trust a tool
   name from documentation, including this skill's own reference. The project's
   published docs list several tools and CLI subcommands that do not exist.
3. `screenshot` — evidence, and your own orientation.
4. `inspect_interactive` — the real elements and their refs. Every ref you act
   on must come from this output, never from source code or from a guess.
5. `tap` / `enter_text` using those refs only.
6. `wait_for_element` to assert the transition happened, then `screenshot`.
7. Repeat 4–6 until the flow is covered.
8. **Only then:** write the `integration_test` file from what you observed.
9. Run it: `flutter test integration_test/<file> -d <device>`. It must pass
   before the case is registered.
10. Hand off to the `regression` skill to register and record the case.

**Prerequisite, once per project:** `flutter-skill init <app-dir>` writes
`.flutter-skill.yaml` and installs the testing bridge into the app. Run
`flutter-skill doctor` to check it — it reports the SDK, the available devices
and whether that config is present. If `.flutter-skill.yaml` is missing, say so
and stop rather than launching; without the bridge the app attaches but exposes
nothing useful.

Writing a test file before `inspect_interactive` is a **protocol violation**.
The test must reflect what the app renders, not what the task says it renders.

## Rules

1. **MCP-first, artifact-second.** The hard gate is non-negotiable.
2. **The MCP session is not the deliverable.** flutter-skill has no saved-flow
   replay — there is no file it can re-run. A flow verified only over MCP is
   verified exactly once and is worth nothing next month. Every flow that passes
   must land as an `integration_test` file, which `flutter test` replays
   deterministically and CI can run without an agent.
3. **One test file per case**, named for its registry id:
   `integration_test/TC-004_login_happy_test.dart`. Happy, boundary and failure
   are separate files and separate cases.
4. **Find by `Key`, then by semantic label — never by widget tree position.**
   If an element has no stable key, add one to the app source and say so in
   `## Test results`; position-based finders break on the next layout change and
   fail as a false regression.
5. **Device is explicit.** Pass `-d <device-id>` and record that exact id in the
   case's `command`. "Whatever was booted" is not reproducible.
6. **Screenshot every meaningful state transition** and attach the paths to
   `## Test results`.
7. **No sleeps.** Use `wait_for_element` / `tester.pumpAndSettle()`. A fixed
   `sleep` is the single most common source of a flaky case, and a flaky case
   trains everyone to ignore the gate.
8. **Seed fixtures explicitly.** Anything the flow needs — a user, a row, a
   feature flag — is set up in the test's `setUp` or named in the case's
   `fixtures`. Never depend on state a previous test left behind.
9. **One retrieve() call.** Already handled by the tester role.
10. **`flutter_skill` vs `flutter-skill`.** The binary name differs by install
    method (pub.dev ships `flutter_skill`, npm ships `flutter-skill`). Probe once
    with `command -v` and use whichever exists; do not hardcode the other.

## Patterns

### MCP exploration loop
```
launch_app(project_path:"mobile", device_id:"<sim id>")   → build, install, attach
   ↳ on E303: scan_and_connect(port_start:49000, port_end:65000)
→ get_connection_status()           → confirm before anything else
→ tools/list                        → the interaction tools exist only now
→ screenshot()                      → see current screen
→ inspect_interactive()             → real refs, e.g. "input:Email", "button:Login"
→ enter_text(ref:"input:Email", text:"qa@example.test")
→ tap(ref:"button:Login")
→ wait_for_element(key:"DashboardScreen")
→ screenshot()                      → evidence
→ THEN write integration_test/TC-NNN_<slug>_test.dart
→ flutter test integration_test/TC-NNN_<slug>_test.dart -d <device>
```

### Generated test shape
```dart
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('TC-004 valid credentials reach the dashboard', (tester) async {
    await tester.pumpWidget(const MyApp());
    await tester.pumpAndSettle();

    await tester.enterText(find.byKey(const Key('email')), 'qa@example.test');
    await tester.enterText(find.byKey(const Key('password')), 'fixed-test-pw');
    await tester.tap(find.byKey(const Key('loginButton')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('DashboardScreen')), findsOneWidget);
    expect(find.text('Invalid credentials'), findsNothing);
  });
}
```

### Handoff to the registry
```bash
ID=$(sh .claude/py .claude/hooks/lib/test_registry.py next-id)
# case command: "flutter test integration_test/${ID}_login_happy_test.dart -d ios-17-iphone15"
# case runner:  flutter-skill      case surface: e2e-mobile
sh .claude/py .claude/hooks/lib/test_registry.py validate
sh .claude/py .claude/hooks/lib/test_registry.py record "$ID" --result pass
```

## Planner hints

- Assign `flutter-skill` together with `regression` on any Flutter tester task —
  execution and recording are two skills on purpose, and a tester given only
  `flutter-skill` will verify a flow and leave no durable trace of it.
- Name the target device in the task when it matters (`-d chrome` for Flutter
  web, a specific simulator id for iOS). The tester records whatever it is given.
- A flow that cannot be expressed as an `integration_test` — anything crossing
  into another app, the OS settings, or a real payment sheet — should be planned
  as a manual check, not as a tester task. Say so in the task rather than letting
  the tester discover it at step 8.

## Anti-patterns

- Ending at a green MCP session. Nothing was captured; the next run starts over.
- Writing the test file from the task description or from reading the source.
- `find.byType(TextField).at(1)` — positional finders that break on layout edits.
- `await Future.delayed(...)` instead of `pumpAndSettle` / `wait_for_element`.
- Reusing one test file for happy, boundary and failure — a red bar then names
  the file, not the behaviour.
- Registering the case before `flutter test` passes locally.
- Hardcoding a device that only exists on the author's machine.

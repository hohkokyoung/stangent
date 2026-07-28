# Turning an MCP session into a durable integration_test

The MCP session tells you what the app actually does. The `integration_test`
file is the only part that outlives the session. This is the translation.

## Setup, once per project

```yaml
# pubspec.yaml
dev_dependencies:
  integration_test:
    sdk: flutter
  flutter_test:
    sdk: flutter
```

```
integration_test/
  TC-004_login_happy_test.dart
  TC-005_login_wrong_password_test.dart
```

Run one file:

```bash
flutter test integration_test/TC-004_login_happy_test.dart -d ios-17-iphone15
```

That exact string — device id included — is what goes in the registry case's
`command`. `flutter test integration_test` runs the whole suite and is what CI
should call.

## Ref → finder

`inspect_interactive` returns semantic refs. They are for driving the live app,
not for the test file. Translate them:

| MCP ref | Finder in the test | Note |
|---|---|---|
| `button:Login` | `find.byKey(const Key('loginButton'))` | preferred — stable across copy changes |
| `button:Login` | `find.widgetWithText(ElevatedButton, 'Login')` | acceptable when no key exists |
| `input:Email` | `find.byKey(const Key('email'))` | |
| — | `find.byType(TextField).at(1)` | **never** — breaks on any layout edit |

If the element has no key, add one to the app source. That is a real change to
the product code and belongs in `## Test results` so the reviewer sees it; it is
still far cheaper than a positional finder that fails as a false regression six
weeks from now.

## Wait → assert

| In the session | In the test |
|---|---|
| `wait_for_element(key: 'DashboardScreen')` | `await tester.pumpAndSettle();`<br>`expect(find.byKey(const Key('DashboardScreen')), findsOneWidget);` |
| `assert_gone('Invalid credentials')` | `expect(find.text('Invalid credentials'), findsNothing);` |
| `get_text(ref: 'label:Total')` then eyeballing it | `expect(find.text('RM 42.00'), findsOneWidget);` |

`pumpAndSettle` waits for animations to finish. It does **not** wait for a
network call — for that, pump a fixed fake rather than a real request:

```dart
await tester.runAsync(() => mockApi.settleAll());
await tester.pumpAndSettle();
```

## Determinism checklist

Everything here is a rerun failure waiting to happen. Check each before
registering the case.

- [ ] No `Future.delayed`, no `sleep`. `pumpAndSettle` or an explicit pump loop.
- [ ] No real network. Inject a fake client in `setUp`.
- [ ] No `DateTime.now()` in an assertion. Inject a fixed clock, or assert on a
      format rather than a value.
- [ ] No dependence on data another test created. Each test seeds its own state.
- [ ] No locale/timezone assumption. Set them explicitly if the assertion is
      locale-sensitive (currency, dates).
- [ ] Device id pinned in the command.
- [ ] Runs green twice in a row from a cold start.

The last one is the only one that is actually evidence. Run it twice.

## Shape

```dart
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  setUp(() async {
    await seedUser(email: 'qa@example.test', password: 'fixed-test-pw');
  });

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

The `testWidgets` description starts with the case id. When CI prints a failing
test name, that is the only thing connecting it back to the registry entry that
explains why the case exists.

## Flutter web

`-d chrome` works, with two differences: `surface` in the registry is
`e2e-web`, not `e2e-mobile`, and text input goes through the browser, so
`enterText` on a semantics node can behave differently than on mobile. Verify
the flow over MCP against the web target specifically — do not assume a passing
iOS case implies a passing web one.

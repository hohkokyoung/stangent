# Flutter Widget Testing

## tester.view — current API (Flutter 3.10+)

```dart
// CURRENT
testWidgets('mobile viewport', (tester) async {
  tester.view.devicePixelRatio = 3.0;
  tester.view.physicalSize = const Size(390 * 3, 844 * 3); // logical × DPR
  tester.view.padding = FakeViewPadding.zero;
  tester.view.viewInsets = FakeViewPadding.zero;
  addTearDown(tester.view.reset); // REQUIRED — always reset
});

// DEPRECATED — do not generate
tester.binding.window.physicalSizeTestValue = const Size(390, 844);
addTearDown(tester.binding.window.clearAllTestValues);
```

`physicalSize` is in **physical pixels**, not logical pixels. Multiply logical size by `devicePixelRatio`.

For locale, brightness, text scale:
```dart
tester.platformDispatcher.textScaleFactorTestValue = 1.5;
tester.platformDispatcher.platformBrightnessTestValue = Brightness.dark;
addTearDown(tester.platformDispatcher.clearAllTestValues);
```

## Pump strategies

| Method | When | Gotcha |
|---|---|---|
| `pump()` | Advance one frame | Does not resolve Futures |
| `pump(Duration)` | Advance clock by duration | For animations with known length |
| `pumpAndSettle()` | Wait for all animations | **Times out on infinite animations** (`CircularProgressIndicator`) |
| `runAsync(() async { ... })` | Real async I/O | Leaves fake-time zone |

```dart
// pumpAndSettle HANGS on infinite animations — use pump() instead
await tester.pump(); // render one frame

// pumpAndSettle does NOT wait for network calls
await tester.tap(find.byType(ElevatedButton));
await tester.pump();         // trigger Future start
await tester.pump();         // rebuild after mocked Future resolves
```

## pumpAndSettle + Riverpod async = infinite loop

```dart
// WRONG — hangs if provider stays in AsyncLoading
await tester.pumpWidget(ProviderScope(child: MyWidget()));
await tester.pumpAndSettle();

// CORRECT — pin state with overrideWithValue
await tester.pumpWidget(
  ProviderScope(
    overrides: [dataProvider.overrideWithValue(AsyncValue.data(myData))],
    child: MyWidget(),
  ),
);
await tester.pump(); // one frame is enough
```

## Deprecated APIs — removed or flagged for removal

```dart
// REMOVED in Flutter 3.22 — compile errors, not warnings
Theme.of(context).textTheme.headline1  // → displayLarge
Theme.of(context).textTheme.bodyText1  // → bodyLarge
ThemeData(errorColor: ...)             // → colorScheme.error
MediaQuery.boldTextOverride(context)   // → MediaQuery.boldTextOf(context)
```

## physicalSize gotcha

```dart
// WRONG — treats physicalSize as logical
tester.view.physicalSize = const Size(375, 812); // wrong at 3x DPR

// CORRECT for iPhone 13 (375×812 logical at 3x)
tester.view.devicePixelRatio = 3.0;
tester.view.physicalSize = const Size(375 * 3, 812 * 3);
```

## TestTextInput resets between tests

```dart
// Text field state is reset between tests in Flutter 3.x.
// Never rely on field state from a previous test — set it explicitly.
```

## Running tests

```bash
flutter test                                        # all tests
flutter test test/widgets/my_widget_test.dart       # single file
flutter test --name "button renders"                # by name substring
flutter test --update-goldens                       # regenerate golden files
flutter test --coverage                             # output: coverage/lcov.info
flutter test -j 4                                   # concurrency
flutter test integration_test/                      # integration tests
```

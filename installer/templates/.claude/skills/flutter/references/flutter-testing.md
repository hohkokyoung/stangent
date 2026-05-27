# Flutter testing with Riverpod

## ProviderScope overrides
```dart
testWidgets('shows name', (tester) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        apiClientProvider.overrideWithValue(FakeApi()),
      ],
      child: const MaterialApp(home: ProfileScreen()),
    ),
  );
  await tester.pumpAndSettle();
  expect(find.text('Ada'), findsOneWidget);
});
```

## Overriding AsyncNotifier
Use `overrideWith` to swap the notifier's build behavior:
```dart
userProfileProvider.overrideWith(() => FakeUserProfile()..stub = AsyncValue.data(fakeUser)),
```

## Pump strategy
- `pump()` — advance one frame.
- `pumpAndSettle()` — advance until no more animations. Don't use during infinite-running animations.
- `pump(const Duration(seconds: 1))` — controlled time advance.

## Golden tests
Use sparingly. Pin device/font/theme; otherwise diffs are noisy.

## Don't
- Don't await futures inside the widget tree from a test — control them via fakes that complete deterministically.
- Don't share a single `ProviderContainer` across tests.

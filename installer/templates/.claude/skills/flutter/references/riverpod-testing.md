# Riverpod 3.x Testing

## ProviderContainer.test() — Riverpod 3.0+

```dart
// RIVERPOD 2.x — manual tearDown required
ProviderContainer createContainer({List<Override> overrides = const []}) {
  final container = ProviderContainer(overrides: overrides);
  addTearDown(container.dispose);
  return container;
}

// RIVERPOD 3.0+ — auto-disposes after test, no tearDown needed
test('example', () {
  final container = ProviderContainer.test(
    overrides: [myProvider.overrideWith((ref) => 'mocked')],
  );
  expect(container.read(myProvider), 'mocked');
});
```

## overrideWith vs overrideWithValue

```dart
// overrideWith — builder function; works for all provider types
final container = ProviderContainer.test(
  overrides: [
    myProvider.overrideWith((ref) => 'hello'),
    myFutureProvider.overrideWith((ref) async => 42),
  ],
);

// overrideWithValue — pin a raw value directly
final container = ProviderContainer.test(
  overrides: [
    myStringProvider.overrideWithValue('direct'),
    // For FutureProvider — use AsyncValue (restored in Riverpod 3.0)
    myFutureProvider.overrideWithValue(AsyncValue.data(42)),
    myFutureProvider.overrideWithValue(AsyncValue.loading()),
    myFutureProvider.overrideWithValue(AsyncValue.error(Exception('fail'), StackTrace.empty)),
  ],
);

// Same in ProviderScope for widget tests
await tester.pumpWidget(
  ProviderScope(
    overrides: [myFutureProvider.overrideWithValue(AsyncValue.data(42))],
    child: const MyWidget(),
  ),
);
```

## overrideWithBuild — Riverpod 3.0 only

Overrides only `build()`, preserving all other notifier methods:

```dart
final container = ProviderContainer.test(
  overrides: [
    counterNotifierProvider.overrideWithBuild((ref, self) => 100),
  ],
);
container.read(counterNotifierProvider.notifier).increment(); // real method works
expect(container.read(counterNotifierProvider), 101);
```

## Faking AsyncNotifier with mockito

```dart
import 'package:mockito/mockito.dart';

class MockAuthController extends _$AuthController with Mock implements AuthController {}

void main() {
  setUpAll(() {
    // REQUIRED — register fallbacks for AsyncValue matchers
    registerFallbackValue(const AsyncLoading<void>());
    registerFallbackValue(const AsyncData<void>(null));
  });

  test('sign-in success', () async {
    final mockRepo = MockAuthRepository();
    when(mockRepo.signIn(any, any)).thenAnswer((_) async {});

    final container = ProviderContainer.test(
      overrides: [authRepositoryProvider.overrideWithValue(mockRepo)],
    );

    final listener = Listener<AsyncValue<void>>();
    container.listen(authControllerProvider, listener, fireImmediately: true);
    verify(() => listener(null, const AsyncData<void>(null)));

    await container.read(authControllerProvider.notifier).signIn('a@b.com', 'pw');

    verifyInOrder([
      () => listener(const AsyncData<void>(null), any(that: isA<AsyncLoading>())),
      () => listener(any(that: isA<AsyncLoading>()), const AsyncData<void>(null)),
    ]);
  });
}
```

## AsyncLoading equality gotcha

```dart
// WRONG — AsyncLoading carries previous data; equality check often fails
verify(() => listener(prev, AsyncLoading<void>()));

// CORRECT — use isA matcher
verify(() => listener(prev, any(that: isA<AsyncLoading<void>>())));
```

## tester.container() — Riverpod 3.0+

```dart
testWidgets('access container from widget test', (tester) async {
  await tester.pumpWidget(const ProviderScope(child: MyApp()));
  final container = tester.container();
  expect(container.read(myProvider), 'expected');
  container.read(counterProvider.notifier).increment();
  await tester.pump();
  expect(find.text('1'), findsOneWidget);
});
```

## Container sharing = state leakage

```dart
// WRONG — shared container leaks state between tests
final container = ProviderContainer(); // module-level

// CORRECT — fresh container per test (Riverpod 3.0)
test('each test', () {
  final container = ProviderContainer.test(); // isolated, auto-disposed
});
```

## Removed in Riverpod 3.0

```dart
// REMOVED — AutoDisposeNotifier and FamilyNotifier are merged into Notifier
class MyNotifier extends AutoDisposeNotifier<int> { ... }  // 2.x — gone
class MyNotifier extends Notifier<int> { ... }             // 3.0+ — Notifier IS auto-dispose

// REMOVED — AsyncValue.valueOrNull
final val = asyncValue.valueOrNull; // 2.x
final val = asyncValue.value;       // 3.0+

// MOVED to package:riverpod/legacy.dart (avoid in new code)
import 'package:riverpod/legacy.dart'; // StateProvider, StateNotifierProvider
```

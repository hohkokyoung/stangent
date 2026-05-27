# AsyncNotifier patterns (Riverpod 3.x)

## Basic AsyncNotifier
```dart
@riverpod
class UserProfile extends _$UserProfile {
  @override
  Future<UserDto> build() async {
    return ref.read(apiClientProvider).getMe();
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    state = await AsyncValue.guard(() => ref.read(apiClientProvider).getMe());
  }

  Future<void> updateName(String name) async {
    state = const AsyncValue.loading();
    state = await AsyncValue.guard(
      () => ref.read(apiClientProvider).updateMe(name: name),
    );
  }
}
```

Note: in Riverpod 3, `ref` inside the notifier is a plain `Ref` (no generated typedef). Build methods can be `Future<T>` (yields `AsyncValue<T>`) or `T` (yields plain `T`).

## Why AsyncValue.guard
It captures both `data` and `error` into the `AsyncValue`. Hand-written try/catch tends to forget the error path, leaving UI stuck on `loading`.

## Optimistic update
```dart
Future<void> setName(String name) async {
  final previous = state;
  if (previous case AsyncData(:final value)) {
    state = AsyncData(value.copyWith(name: name));
  }
  state = await AsyncValue.guard(
    () => ref.read(apiClientProvider).updateMe(name: name),
  );
  // on failure, state already contains the error from guard
}
```

## Family AsyncNotifier
For per-id state, add params to `build`:
```dart
@riverpod
class UserById extends _$UserById {
  @override
  Future<UserDto> build(int id) => ref.read(apiClientProvider).getUser(id);
}
```

## Don't
- Don't call `notifier.method()` from `build`. Mutations belong in callbacks.
- Don't `await` notifier methods from `build` either — it deadlocks.
- Don't reference Riverpod 2.x `*Ref` typedefs — they're gone in v3.

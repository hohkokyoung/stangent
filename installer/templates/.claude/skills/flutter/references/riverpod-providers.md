# Riverpod providers (3.x, codegen)

## Provider types
- `@riverpod` function provider — synchronous derived value, or async result with `Future<T>` return.
- `@riverpod` class provider — stateful, exposes a notifier. Use `AsyncNotifier` for async state.
- `@Riverpod(keepAlive: true)` — opt out of auto-dispose. Use sparingly with a documented reason.

In Riverpod 3, **`Ref` is no longer generic** — generated `*Ref` typedefs (`MyProviderRef`) are gone. Build methods receive a plain `Ref`.

## Function provider example
```dart
@riverpod
ApiClient apiClient(Ref ref) {
  final token = ref.watch(authTokenProvider);
  return ApiClient(token: token);
}
```

## Family providers
Parameterize a provider — additional positional params after `ref`:
```dart
@riverpod
Future<User> userById(Ref ref, int id) async {
  return ref.read(apiClientProvider).getUser(id);
}
```
Consumed as `ref.watch(userByIdProvider(42))`.

## Codegen workflow
1. Annotate.
2. Run `dart run build_runner watch -d` during development.
3. Generated file is `*.g.dart`; commit it.

## Don't
- Don't write `final myProvider = StateProvider((ref) => ...)` by hand. Use the annotation.
- Don't import or reference `MyProviderRef` — those typedefs no longer exist in Riverpod 3.
- Don't access providers via `ProviderContainer` outside of tests / app bootstrap.

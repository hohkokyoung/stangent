# SKILL: flutter

## Purpose
Governs Flutter UI code using Riverpod **3.x** for state management. Scope: providers, Notifier/AsyncNotifier with codegen, ConsumerWidget composition, Ref usage. Does NOT cover navigation, theming, or non-Riverpod state.

## Rules

1. **Riverpod 3.x with codegen.** Use `@riverpod` / `@Riverpod(keepAlive: ...)` annotations + `build_runner`. No manual `Provider(...)` declarations.
2. **`Ref` is non-generic in Riverpod 3.** Notifier/provider build methods receive a plain `Ref` — not `MyProviderRef` (that was 2.x). Generated `*Ref` typedefs are gone; do not import or reference them.
3. **AsyncNotifier for async state.** Anything that loads, mutates, or refreshes server data extends `_$MyNotifier` (codegen base) and exposes `AsyncValue<T>`.
4. **Never call `ref.read` inside `build`.** Use `ref.watch` in `build`. Use `ref.read` only in callbacks (onPressed, etc.) and inside notifier methods.
5. **Mutations update `state` via `AsyncValue.guard`.** Never set `state = AsyncValue.data(...)` from a hand-written try/catch — `guard` is the rule.
6. **ConsumerWidget over StatefulWidget.** Use `StatefulWidget` only when you genuinely need lifecycle hooks beyond what `ref` provides.
7. **Widgets render `AsyncValue` explicitly.** Pattern-match with `.when(data:, loading:, error:)`. Do NOT throw on error states — render an error UI.
8. **Auto-dispose is the default in Riverpod 3.** Use `@Riverpod(keepAlive: true)` only with a written reason.
9. **Use `ref.listen` for side effects** (snackbars, navigation) — never inside `build`.

## Patterns

- **AsyncNotifier (Riverpod 3 shape):**
  ```dart
  @riverpod
  class UserProfile extends _$UserProfile {
    @override
    Future<UserDto> build() async {
      // `ref` is plain Ref in v3 — no UserProfileRef alias
      return ref.read(apiClientProvider).getMe();
    }

    Future<void> updateName(String name) async {
      state = const AsyncValue.loading();
      state = await AsyncValue.guard(
        () => ref.read(apiClientProvider).updateMe(name: name),
      );
    }
  }
  ```
- **Widget consumption:**
  ```dart
  class ProfileScreen extends ConsumerWidget {
    @override
    Widget build(BuildContext context, WidgetRef ref) {
      final profile = ref.watch(userProfileProvider);
      return profile.when(
        data: (u) => ProfileView(user: u),
        loading: () => const CircularProgressIndicator(),
        error: (e, st) => ErrorView(error: e),
      );
    }
  }
  ```
- **Mutation trigger:**
  ```dart
  onPressed: () => ref.read(userProfileProvider.notifier).updateName(newName),
  ```

## Anti-patterns

- Importing or declaring `MyProviderRef` typedefs — removed in Riverpod 3.
- `ref.read` inside `build` (causes silent staleness).
- Manual try/catch inside a notifier method to set `state` — bypasses `AsyncValue.guard`.
- Throwing from a widget on `AsyncValue.error` instead of rendering an error widget.
- Using `StatefulWidget` to hold data that belongs in a provider.
- Hand-writing `final myProvider = StateNotifierProvider(...)` instead of using `@riverpod`.
- Calling notifier mutations from `build` — only callbacks.
- `keepAlive: true` without a documented reason.
- Carrying over Riverpod 2.x patterns: generic `Ref<T>` constraints, `StateNotifier` base classes.

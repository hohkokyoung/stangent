# Widget composition with Riverpod

## ConsumerWidget vs StatefulWidget
- `ConsumerWidget` (or `HookConsumerWidget` with flutter_hooks): default. Gives you `ref`.
- `StatefulWidget` only when you need lifecycle hooks (`AnimationController`, focus nodes) not naturally expressed via providers.
- `ConsumerStatefulWidget` when you need both.

## Render AsyncValue
Always pattern-match. Never assume data:
```dart
final profile = ref.watch(userProfileProvider);
return profile.when(
  data: (u) => ProfileView(user: u),
  loading: () => const LoadingShimmer(),
  error: (e, _) => ErrorRetry(error: e, onRetry: () => ref.invalidate(userProfileProvider)),
);
```

## Listening for side-effects (e.g. snackbar)
```dart
ref.listen<AsyncValue<UserDto>>(userProfileProvider, (prev, next) {
  if (next is AsyncError) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('${next.error}')));
  }
});
```

## Avoid rebuild storms
- `ref.watch(provider.select((s) => s.field))` — rebuild only when `field` changes.
- Split widgets so each subtree watches the smallest provider it needs.

## Don't
- Don't pass providers via constructor args. Read them via `ref` inside the widget.
- Don't `setState` inside a `ConsumerWidget`; it has no `setState`.
- Don't throw on `AsyncError` — render an error state.

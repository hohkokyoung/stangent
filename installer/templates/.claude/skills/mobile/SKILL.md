# SKILL: mobile

## Purpose
Governs app-wide UX coherence on mobile: how state changes in one part of the app stay consistent across every screen that depends on that state. Framework-agnostic — applies to Flutter, React Native, Swift (UIKit/SwiftUI), and Kotlin (Compose/XML). Does NOT cover framework mechanics, navigation libraries, or backend data-fetching strategies.

## Rules

1. **State that affects multiple screens lives at the app layer, not the screen layer.** Any value that more than one screen needs to read or react to must be managed in a shared, app-scoped store — not inside a single screen's local state.
2. **Filter and gate at the data layer, never in the widget/view.** If a piece of data should not appear somewhere, it must be excluded before it reaches the UI component. Per-widget conditionals are not a substitute.
3. **Every screen that surfaces a list or collection must declare its dependencies on app-scoped state.** If a screen renders users, posts, or content, it is responsible for knowing which global filters apply (e.g., muted, hidden, unavailable). This declaration must be explicit — implicit reliance on "the data is already clean" is not acceptable.
4. **Optimistic mutations must propagate immediately to all visible screens.** When a user takes an action, every screen currently rendered must reflect it before the server responds. A mutation that only updates the initiating screen is incomplete.
5. **Navigation to an unavailable target must be guarded.** Any route whose content depends on app-scoped state must check that state before rendering. A screen that can be navigated to directly (deep link, search result, notification tap) is not exempt.
6. **Contextual actions (long-press menus, swipe actions, bottom sheets) must be consistent.** The same action on the same type of content must appear identically regardless of which screen the user is on.
7. **Empty states are designed outputs, not fallbacks.** When a filter, guard, or mutation removes all content from a screen, the resulting empty state must be intentional — with a clear message and, where appropriate, a recovery path.
8. **State propagation is synchronous from the user's perspective.** Latency in server confirmation is not a reason for a visual delay. Show the result first; reconcile with the server response afterward.

## Patterns

- **App-scoped state declaration (pseudocode):**
  ```
  // App layer — one source of truth
  appStore.hiddenIds: Set<ID>       // populated by any mute/block/hide action
  appStore.unavailableIds: Set<ID>  // populated by delete/ban/deactivate events

  // Every list provider filters before returning
  FeedProvider.build():
    raw = fetch(feed)
    return raw.filter(item => !appStore.hiddenIds.has(item.authorId))
  ```

- **Optimistic mutation — update all dependents before server responds:**
  ```
  onUserAction(targetId):
    1. appStore.hiddenIds.add(targetId)          // immediate — all screens react
    2. appStore.notify()                          // triggers rebuild on all watchers
    3. api.recordAction(targetId)                 // async — fire and forget
    4. on server error: appStore.hiddenIds.remove(targetId) + show error toast
  ```

- **Navigation guard — check before render:**
  ```
  ProfileRoute.onEnter(userId):
    if appStore.unavailableIds.has(userId):
      render UnavailableScreen("This profile is no longer available")
      return
    render ProfileScreen(userId)
  ```

- **Contextual action sheet — consistent shape:**
  ```
  ContentActionSheet(contentId, authorId):
    actions = [
      if currentUser != authorId: Action("Report", onReport),
      if currentUser != authorId: Action("Hide", onHide),
      if owns(contentId):         Action("Delete", onDelete),
    ]
    show BottomSheet(actions)
  ```

- **Intentional empty state:**
  ```
  FeedScreen:
    if feed.isEmpty and appStore.hiddenIds.size > 0:
      render EmptyState(
        message: "Nothing to show here.",
        subtext: "Your hidden content settings may be affecting this view.",
        action: null   // no recovery path needed unless UX spec says so
      )
  ```

## Anti-patterns

- Holding state that multiple screens need inside a single screen's local state.
- Filtering out hidden or unavailable content inside widget `build` methods rather than in the data/provider layer.
- A mutation that updates only the screen where the action occurred and relies on a page reload elsewhere to sync.
- Navigation to a content screen without checking whether the target is still available.
- Inconsistent contextual action menus across screens for the same content type.
- Treating an empty list after filtering as an error state — it is an expected, designable state.
- Deferring the visual result of a user action until the server responds.
- Duplicating filter logic across multiple screens instead of centralising it in a shared provider.

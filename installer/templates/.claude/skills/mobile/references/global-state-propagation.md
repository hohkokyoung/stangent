# Reference: Global State Propagation

## What it is
Global state propagation is the pattern of ensuring that a single piece of app-level state — a set of hidden IDs, a subscription tier, a user's privacy setting — is consistently reflected across every screen that depends on it, without each screen independently managing or fetching that value.

## When to apply
Any time a feature introduces state that:
- Should affect the content shown on more than one screen
- Can change at runtime (not just at startup)
- Is triggered by a user action in one place but needs to be visible everywhere

Common examples: muting/hiding content, subscription upgrades that unlock features, account verification status, feature flags.

## The pattern

### 1. Single source of truth at the app layer
The state lives in exactly one place — an app-scoped store, a global provider, or a top-level state container. Every screen reads from that one place.

```
appStore:
  hiddenIds: Set<ID>
  userTier: Tier
  verifiedStatus: Bool
```

### 2. Screens declare dependencies, they do not own the state
A screen that needs to know about hidden content does not fetch or store the hidden list itself. It watches the app store and reacts when the store changes.

```
FeedScreen:
  watch(appStore.hiddenIds)       // re-renders whenever hiddenIds changes
  watch(feedData)

  build():
    filtered = feedData.filter(item => !appStore.hiddenIds.has(item.id))
    render List(filtered)
```

### 3. One action updates the store; all screens react automatically
No screen needs to be explicitly notified. Updating the store triggers rebuilds in all watchers.

```
onHideAction(targetId):
  appStore.hiddenIds.add(targetId)   // one write, all screens rebuild
```

## Checklist — planner / implementer

- [ ] Where does this state live? Is it app-scoped or screen-scoped?
- [ ] Which screens need to read this state?
- [ ] Does any screen currently re-fetch or re-derive the state independently? (If yes, centralise.)
- [ ] What triggers a change to this state? Is the trigger always app-layer or can it come from a push notification / server event?
- [ ] What is the initial value at app start? Is it hydrated from local storage or from the server?

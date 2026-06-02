# Reference: Navigation Guards

## What it is
A navigation guard is a check that runs before a screen renders, verifying that the target content is accessible given current app state. If the check fails, the user sees a designed fallback screen instead of broken or forbidden content.

## When to apply
Any route whose content depends on state that can change at runtime:
- A profile that might be deactivated, banned, or set to private
- A post that might be deleted or hidden
- A feature screen that requires a subscription tier the user may not have
- A settings screen that requires account verification

Any screen that can be reached via a deep link, push notification tap, or search result — not just in-app navigation — must have a guard. Users can land on these screens from outside the app's normal flow.

## The pattern

### Guard runs before render
```
ProfileRoute.onEnter(userId):
  // Check 1: is the target available?
  if appStore.unavailableIds.has(userId):
    render UnavailableScreen(
      message: "This profile is no longer available.",
      action: GoBack
    )
    return

  // Check 2: does the current user have access?
  if profile.isPrivate and !currentUser.follows(userId):
    render PrivateProfileScreen(userId)   // partial view, not full profile
    return

  // All checks passed
  render ProfileScreen(userId)
```

### Guard is synchronous where possible
Read from app-scoped state first (instant, no network). Only hit the network if local state is insufficient.

```
ProfileRoute.onEnter(userId):
  // Fast path — check local state first
  if appStore.unavailableIds.has(userId):
    render UnavailableScreen(...)
    return

  // Slow path — fetch and check
  profile = await api.getProfile(userId)
  if profile.notFound or profile.banned:
    appStore.unavailableIds.add(userId)  // cache for future navigations
    render UnavailableScreen(...)
    return

  render ProfileScreen(profile)
```

## Fallback screen design

| Condition | Fallback screen |
|---|---|
| Target deleted or deactivated | "This content is no longer available." + Go back |
| Target is private / restricted | Partial view (avatar + name, no content) + Follow request option |
| Feature requires upgrade | Paywall / upgrade prompt |
| Target is unavailable (network) | Retry state, not an error crash |

## Checklist — planner / implementer

- [ ] Which screens in this feature can be reached via deep link or external tap (not just in-app navigation)?
- [ ] What conditions make the target content unavailable or restricted?
- [ ] Is the check synchronous (from app state) or async (requires a network call)?
- [ ] What does the fallback screen look like? Is it designed, not just a blank page?
- [ ] Is unavailability cached in app state so repeated navigation doesn't re-fetch?
- [ ] Does the guard cover all entry points to the screen, not just the primary in-app path?

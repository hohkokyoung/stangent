# Reference: Optimistic UI

## What it is
Optimistic UI is the pattern of immediately applying the visual result of a user action — before the server confirms it — and then reconciling with the server response afterward. From the user's perspective, the app responds instantly. The network call is a background concern.

## When to apply
- Any mutation that the user expects to feel instant (hide, like, follow, delete, submit)
- Any action where the user would notice a delay between tapping and seeing a result
- Any action that affects content visible on the current screen

Do NOT apply optimistic UI to actions where an error from the server would be unrecoverable or confusing to undo (e.g., a payment, an irreversible deletion of important data). In those cases, show a loading state and wait for confirmation.

## The pattern

### Step 1: Apply locally, immediately
Update the app-scoped store or local state before the API call.

```
onHide(targetId):
  appStore.hiddenIds.add(targetId)     // instant — UI updates now
  call api.hide(targetId)              // async — no await before the UI update
```

### Step 2: Handle server response
- **Success:** No UI change needed — state is already correct.
- **Error:** Roll back the local change and surface a recoverable error.

```
result = await api.hide(targetId)
if result.isError:
  appStore.hiddenIds.remove(targetId)  // undo
  showToast("Something went wrong. Try again.")
```

### Step 3: Propagation is automatic
Because the change was made to the app-scoped store (not the screen's local state), every screen watching that store rebuilds automatically. No manual notification needed.

## Rollback design
A rollback must:
- Restore the exact previous state (snapshot before the mutation, not a re-fetch)
- Surface a non-blocking error (toast, snackbar) — not a modal or navigation
- Leave the user on the same screen in the same position

```
// Snapshot before mutating
previousHidden = Set.copy(appStore.hiddenIds)

appStore.hiddenIds.add(targetId)

result = await api.hide(targetId)
if result.isError:
  appStore.hiddenIds = previousHidden   // restore snapshot
  showToast("Couldn't complete action.")
```

## Checklist — planner / implementer

- [ ] Is this action a good candidate for optimistic UI? (instant-feeling, reversible on error)
- [ ] What is the exact state change to apply before the API call?
- [ ] What is the rollback if the API returns an error?
- [ ] Does the rollback restore state correctly without a re-fetch?
- [ ] Is the error surfaced non-disruptively (toast/snackbar, not a modal)?
- [ ] Are all screens that watch the affected state automatically updated, or does manual notification exist?

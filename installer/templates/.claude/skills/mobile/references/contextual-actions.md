# Reference: Contextual Actions

## What it is
A contextual action is an in-place action on a piece of content — triggered by long-press, swipe, tap on a menu icon, or a floating action. The canonical mobile pattern is a bottom sheet or action sheet listing available actions for that item. The actions vary by context (what the content is, who owns it, what the current user's relationship to it is).

## When to apply
Any time content in a list or on a detail screen can be acted on without navigating away. Common contexts:
- Posts, comments, replies in a feed
- User profile cards in a list
- Media items in a gallery
- Messages in a chat

## The pattern

### Action availability is computed, not hardcoded
The set of actions shown depends on: content type, ownership, and relationship to the current user.

```
computeActions(content, currentUser):
  actions = []

  if content.authorId == currentUser.id:
    actions.add(Action("Edit",   onEdit))
    actions.add(Action("Delete", onDelete))
  else:
    actions.add(Action("Report", onReport))
    actions.add(Action("Hide",   onHide))

  if content.isShareable:
    actions.add(Action("Share",  onShare))

  return actions
```

### The same action on the same content type looks and behaves identically across all screens
A "Hide post" action on the feed screen and on the profile screen must:
- Have the same label
- Call the same handler (or delegate to the same app-layer function)
- Produce the same result

This is enforced by centralising action definitions — not duplicating them per screen.

```
// Defined once at app layer
PostActions:
  hide(postId):   appStore.hiddenIds.add(postId); api.hide(postId)
  report(postId): showReportFlow(postId)
  delete(postId): appStore.deletedIds.add(postId); api.delete(postId)

// Used by every screen that surfaces posts
FeedItem.onLongPress:   show ActionSheet(PostActions.forPost(post, currentUser))
ProfilePost.onLongPress: show ActionSheet(PostActions.forPost(post, currentUser))
```

### Bottom sheet structure
```
ActionSheet:
  header: optional — content title or author name (gives context)
  actions: list of labelled, icon-paired items
  destructive actions: visually distinct (red label)
  cancel: always present, always last
```

### Destructive actions require confirmation
Any action that cannot be undone (delete, ban, permanent remove) must show a confirmation before executing.

```
onDelete(contentId):
  show ConfirmationDialog(
    message: "Delete this post? This cannot be undone.",
    confirm: Action("Delete", destructive: true, handler: executeDelete),
    cancel:  Action("Cancel")
  )
```

Non-destructive actions (hide, mute, report) execute immediately without confirmation — the friction should match the severity.

## Checklist — planner / implementer

- [ ] What content types in this feature support contextual actions?
- [ ] What is the full set of actions per content type, and which are conditional on ownership or relationship?
- [ ] Are action definitions centralised (one place) or duplicated per screen?
- [ ] Are destructive actions visually distinct and confirmed before execution?
- [ ] Do non-destructive actions execute immediately (no confirmation dialog)?
- [ ] Is the bottom sheet / action sheet consistent in shape across all screens that use it?
- [ ] After an action executes, what does the user see? (item removed, item updated, toast, nothing?)

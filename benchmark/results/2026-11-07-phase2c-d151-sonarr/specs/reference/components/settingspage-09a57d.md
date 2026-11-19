# Settings Page

`ts|frontend/src/Settings/SettingsPage.tsx#SettingsPage` · component · sonarr

From the code: SettingsPage

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page shows a titled content area with a toolbar, the page's own content below it, and a pending-changes confirmation dialog. [1]
- **S2** The toolbar always shows a pinned advanced settings button with its label shown. [2]
- **S3** The toolbar shows a Save item with a save icon unless showSave is turned off (it defaults to on). [2]
- **S4** The Save item is labelled with the translation of SaveChanges when there are pending changes and with the translation of NoChanges otherwise. [2][3]
- **S5** The Save item spins while saving. [2]
- **S6** The unsaved-changes dialog has the header 'UnsavedChanges' and the message 'PendingChangesMessage', each looked up as a translation key. It has two buttons: 'PendingChangesStayReview' and 'PendingChangesDiscardChanges'. [4][3]
- **S7** The dialog is open only while navigation is blocked. [5]

## Actions

- **A1** Pressing Save calls the page's save handler. [2]
- **A2** The save keyboard shortcut calls the save handler when there are pending changes. It works globally, including inside input fields. [5][6]
- **A3** In the unsaved-changes dialog, the stay-and-review button cancels the navigation and closes the dialog. [4][5]
- **A4** In the unsaved-changes dialog, the discard button lets the blocked navigation go ahead. [4][5]
- **A5** Closing the dialog is treated as a cancel. [4]
- **A6** While the dialog is open, the accept-confirm-modal shortcut triggers the discard action. [4]

## Rules

- **R1** The Save item is disabled when there are no pending changes. [2]
- **R2** Navigating away is blocked only while there are pending changes. [5]
- **R3** By default showSave is true, and isSaving and hasPendingChanges are false. [1]
- **R4** The discard button in the dialog is focused automatically. [4]

## Side effects

- **E1** The save shortcut calls preventDefault on the key event. [5]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on S4 (unsupported): The source only shows the translation keys SaveChanges and NoChanges. The translated texts 'Save Changes' and 'No Changes' are not in the slice, because the translations table is not shown. **fixed**: Removed the literal texts 'Save Changes' and 'No Changes'; the statement now refers only to the translations of the keys SaveChanges and NoChanges.

## Cited symbols

1. `ts|frontend/src/Settings/SettingsPage.tsx#SettingsPage`
2. `ts|frontend/src/Settings/SettingsSaveItems.tsx#SettingsSaveItems`
3. `ts|frontend/src/Utilities/String/translate.ts#translate`
4. `ts|frontend/src/Settings/PendingChangesModal.tsx#PendingChangesModal`
5. `ts|frontend/src/Settings/useSettingsSave.ts#useSettingsSave`
6. `ts|frontend/src/Helpers/Hooks/useKeyboardShortcuts.ts#useKeyboardShortcuts`

<sub>commit 0bffefe · inputs 53bc04d · slice of 64 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

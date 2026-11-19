# Connect Settings (Notifications)

`page:/settings/connect` · screen · tsconfig.json

From the code: /settings/connect · NotificationSettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled Connect, with a Connections section whose description is the NotificationsSectionDescription text. [1]
- **S2** The page has no save button (showSave is false). [1]
- **S3** Connections are listed as cards sorted by name (numeric-aware), followed by an Add Connection card. [2][3][4]
- **S4** If connections fail to load, the ConnectionsLoadError message is shown; loading state is shown while fetching. [2]
- **S5** Each connection card shows its name, an Enabled or Disabled status (active or muted dot), and tags if it has any. [5]
- **S6** A card is Enabled when any of its event triggers is on; it shows a label per active trigger the connection supports (e.g. On Grab, On File Import, On Rename, On Health Issue). [5]
- **S7** The On File Upgrade label shows only when On File Import and upgrade are both on; the On Episode File Delete For Upgrade label requires both episode file delete options on. [5]
- **S8** The Add Connection dialog lists available connection types from the schema, shows a loading indicator while loading, and shows an AddConnectionError alert on failure. [6][7]
- **S9** Each connection type item shows its name, a Presets menu when presets exist, and a More Info link. [8]
- **S10** The edit dialog has a Name field, notification triggers checkboxes, the connection-specific fields, and Tags, with a message alert if the item has one. [9]
- **S11** The edit dialog title reads Edit Connection or Add Connection with the implementation name, depending on whether an id exists. [9]
- **S12** The Delete button appears in the edit dialog only for existing connections. [9]

## Actions

- **A1** Pressing the Add Connection card opens the Add Connection dialog. [2]
- **A2** Selecting a connection type or preset closes the add dialog and opens the edit dialog for that schema. [2][8]
- **A3** Pressing a connection card opens its edit dialog. [5]
- **A4** Test runs a test of the connection with current values; Save saves it; Cancel closes the dialog. [9][10]
- **A5** Delete in the edit dialog closes it and opens a confirmation (Delete Notification); confirming deletes the connection. [5][11]
- **A6** An advanced settings toggle in the edit dialog controls whether advanced fields are shown. [9][12]

## Rules

- **R1** A new connection starts with its name set to the implementation name, and the triggers On Grab, On File Import, On File Upgrade, On Import Complete, On Rename, On Series Add, On Series Delete, On Episode File Delete, On Episode File Delete For Upgrade, On Application Update and On Manual Interaction Required each on only if the schema supports it. [13]
- **R2** Each trigger checkbox is disabled when the connection type does not support it. [14]
- **R3** On File Upgrade checkbox shows only when On File Import is checked; Episode File Delete For Upgrade only when Episode File Delete is checked; Include Health Warnings only when Health Issue or Health Restored is checked. [14]
- **R4** The edit dialog closes automatically after a save finishes without error. [9]
- **R5** Saving an existing connection with no changes skips testing and all validation; resaving identical settings skips testing; after a warnings-only result, warning validation is skipped on the next save or test. [10]
- **R6** Choosing a schema or preset that isn't found raises an error. [15]

## Side effects

- **E1** Confirming delete sends a DELETE for the connection and removes it from the cached list on success. [16]
- **E2** Saving and testing send the connection settings to the server, including its fields. [10]
- **E3** After a successful save, pending changes and field changes are cleared. [10]

## Couldn't see

- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:403` HTTP DELETE to a URL built at runtime
- the depth limit left out 44 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R1 (unsupported): The defaults cover only some triggers. onHealthIssue and onHealthRestored (and includeHealthWarnings) get no default from the schema's supports flags in useManageConnection, so 'each trigger' is too broad. **fixed**: Rule now lists only the triggers that useManageConnection defaults from the schema's supports flags, excluding health issue/restored and include health warnings.

## Cited symbols

1. `ts|frontend/src/Settings/Notifications/NotificationSettings.tsx#NotificationSettings`
2. `ts|frontend/src/Settings/Notifications/Notifications/Notifications.tsx#Notifications`
3. `ts|frontend/src/Settings/Notifications/useConnections.ts#useSortedConnections`
4. `ts|frontend/src/Utilities/Array/sortByProp.ts#sortByProp`
5. `ts|frontend/src/Settings/Notifications/Notifications/Notification.tsx#Notification`
6. `ts|frontend/src/Settings/Notifications/Notifications/AddNotificationModalContent.tsx#AddNotificationModalContent`
7. `ts|frontend/src/Settings/useProviderSchema.ts#useProviderSchema`
8. `ts|frontend/src/Settings/Notifications/Notifications/AddNotificationItem.tsx#AddNotificationItem`
9. `ts|frontend/src/Settings/Notifications/Notifications/EditNotificationModalContent.tsx#EditNotificationModalContent`
10. `ts|frontend/src/Settings/useProviderSettings.ts#useManageProviderSettings`
11. `ts|frontend/src/Settings/Notifications/useConnections.ts#useDeleteConnection`
12. `ts|frontend/src/Settings/advancedSettingsStore.ts#useShowAdvancedSettings`
13. `ts|frontend/src/Settings/Notifications/useConnections.ts#useManageConnection`
14. `ts|frontend/src/Settings/Notifications/Notifications/NotificationEventItems.tsx#NotificationEventItems`
15. `ts|frontend/src/Settings/useProviderSchema.ts#useSelectedSchema`
16. `ts|frontend/src/Settings/useProviderSettings.ts#useDeleteProvider`

<sub>commit 0bffefe · inputs 363601e · slice of 228 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

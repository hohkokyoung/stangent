# Metadata Settings

`page:/settings/metadata` · screen · tsconfig.json

From the code: /settings/metadata · MetadataSettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Metadata" with a "Settings" scope heading, and a "Metadata" section heading with a description. [1]
- **S2** The page lists metadata providers as cards, sorted by name. [2][3]
- **S3** If loading the list fails, the message "MetadataLoadError" translation is shown; the list shows fetching state until fetched. [2]
- **S4** Each card shows the metadata name and a status of Enabled (active dot) or Disabled (muted dot). [4]
- **S5** When enabled, a card shows a "Metadata" group of labels for metadata-section fields that have a value, and an "Images" group for the other fields that have a value. [4]
- **S6** The edit modal shows an Enable checkbox with help text, the provider's fields, an optional message alert, and Cancel and Save buttons. [5]

## Actions

- **A1** Pressing a metadata card opens its edit modal. [4]
- **A2** In the modal the user can toggle Enable and change provider field values. [5]
- **A3** Save saves the metadata provider; Cancel closes the modal. [5]

## Rules

- **R1** The page's save button is hidden (showSave is false). [1]
- **R2** Provider fields in the modal are disabled when Enable is unchecked. [5]
- **R3** The modal closes automatically after a save finishes without error. [5][6]
- **R4** Advanced settings visibility in the modal follows the show-advanced-settings setting. [7][8]
- **R5** Names are sorted with locale comparison, numeric-aware. [9][3]

## Side effects

- **E1** Saving in the edit modal persists the changed metadata provider settings. [5][10]

## Couldn't see

- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- the depth limit left out 9 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/Metadata/MetadataSettings.tsx#MetadataSettings`
2. `ts|frontend/src/Settings/Metadata/Metadata/Metadatas.tsx#Metadatas`
3. `ts|frontend/src/Settings/Metadata/useMetadata.ts#useSortedMetadata`
4. `ts|frontend/src/Settings/Metadata/Metadata/Metadata.tsx#Metadata`
5. `ts|frontend/src/Settings/Metadata/Metadata/EditMetadataModalContent.tsx#EditMetadataModalContent`
6. `ts|frontend/src/Helpers/Hooks/usePrevious.tsx#usePrevious`
7. `ts|frontend/src/Settings/Metadata/Metadata/EditMetadataModal.tsx#EditMetadataModal`
8. `ts|frontend/src/Settings/advancedSettingsStore.ts#useShowAdvancedSettings`
9. `ts|frontend/src/Utilities/Array/sortByProp.ts#sortByProp`
10. `ts|frontend/src/Settings/Metadata/useMetadata.ts#useManageMetadata`

<sub>commit 0bffefe · inputs 47eb7cf · slice of 116 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

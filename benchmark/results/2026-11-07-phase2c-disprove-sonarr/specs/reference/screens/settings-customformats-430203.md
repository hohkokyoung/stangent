# Custom Formats settings

`page:/settings/customformats` · screen · tsconfig.json

From the code: /settings/customformats · CustomFormatSettingsPage

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled 'Custom Formats' under the 'Settings' scope, with a section heading 'Custom Formats' and a description text, and has no Save button. [1]
- **S2** A toolbar item 'Manage Formats' is shown in the extras group. [1]
- **S3** Custom formats are listed as cards sorted by name, followed by an 'Add Custom Format' card. [2][3]
- **S4** If loading the list fails, the section shows the 'CustomFormatsLoadError' message. [2]
- **S5** Each custom format card shows its name, a status with the conditions count, and a label per condition (default, green if required, red if negated). [4]
- **S6** The status dot is active when the format has at least one condition, otherwise muted. Labels are shown only when there are conditions. [4]
- **S7** The Manage Custom Formats modal shows a loading indicator while first fetching, and an error message if loading fails. [5]
- **S8** The manage modal shows a table with selection checkboxes, name and 'include when renaming' (Yes/No) per row, and an edit button. If there are none, it shows 'No custom formats found' text. [6][7]
- **S9** The edit/add modal has Name and 'Include Custom Format When Renaming' fields, a Conditions list with an 'Add Condition' card, and Delete (existing only), Import, Cancel and Save buttons. [8]
- **S10** Each condition card shows its name, implementation name, and 'Negated' and 'Required' labels when set. [9]
- **S11** The export modal shows the custom format as formatted JSON with a copy-to-clipboard button, a loading indicator, or an error message. [10]

## Actions

- **A1** Pressing 'Manage Formats' opens the Manage Custom Formats modal. [1]
- **A2** Pressing the 'Add Custom Format' card opens an empty edit modal. [2]
- **A3** Pressing a card opens its edit modal; its clone action opens an add modal pre-filled from that format; its export action opens the export modal. [4][2]
- **A4** Delete in the edit modal opens a confirmation; confirming deletes the custom format. [4][11]
- **A5** In the manage modal, columns can be sorted by name or include-when-renaming, toggling direction on repeat press; the default is name ascending. [6]
- **A6** Selected formats can be bulk deleted after confirmation. [6][12]
- **A7** Selected formats can be bulk edited to set 'include when renaming' via an Edit modal with Apply Changes. [6][13][14]
- **A8** In the edit modal, conditions can be added from a schema list, edited, cloned and deleted. [8][15][9][16]
- **A9** Import accepts pasted JSON, resolves conditions against the schema, and fills the edit form with name, rename flag and conditions. [17][8]
- **A10** Saving the custom format calls the save action; the modal closes when saving finishes without error. [8]

## Rules

- **R1** A cloned format gets id 0 and a name from the 'DefaultNameCopiedProfile' text; cloning a missing id throws an error. [16]
- **R2** A cloned condition gets the next id (max id + 1, or 1 if none) and a copied-name default. [16][18]
- **R3** Bulk Delete and Edit buttons are disabled when nothing is selected. [6]
- **R4** Bulk edit sends nothing if 'include when renaming' is left at no change; the modal then closes. [13]
- **R5** Import fails with an error for an unknown condition implementation or unknown option; missing negate/required default to false and missing name to empty. [17]
- **R6** Exported JSON omits some properties and flattens fields to name/value pairs. [19]
- **R7** Existing providers saved with no pending changes skip testing and all validation. [20]
- **R8** The Delete button in the edit modal appears only for an existing format. [8]

## Side effects

- **E1** Bulk edit sends a PUT to the custom format bulk endpoint and updates the cached list with the returned formats. [14][21]
- **E2** Bulk delete sends a DELETE to the custom format bulk endpoint and removes the deleted ids from the cached list. [12][22]
- **E3** Deleting a single custom format sends a DELETE for its id and removes it from the cached list. [11]
- **E4** API requests include X-Api-Key and X-Sonarr-Client headers. [23]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:403` HTTP DELETE to a URL built at runtime
- the depth limit left out 95 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/CustomFormats/CustomFormatSettingsPage.tsx#CustomFormatSettingsPage`
2. `ts|frontend/src/Settings/CustomFormats/CustomFormats/CustomFormats.tsx#CustomFormats`
3. `ts|frontend/src/Settings/CustomFormats/CustomFormats/useCustomFormats.ts#useSortedCustomFormats`
4. `ts|frontend/src/Settings/CustomFormats/CustomFormats/CustomFormat.tsx#CustomFormat`
5. `ts|frontend/src/Settings/CustomFormats/CustomFormats/Manage/ManageCustomFormatsModalContent.tsx#ManageCustomFormatsModalContent`
6. `ts|frontend/src/Settings/CustomFormats/CustomFormats/Manage/ManageCustomFormatsModalContent.tsx#ManageCustomFormatsModalContentInner`
7. `ts|frontend/src/Settings/CustomFormats/CustomFormats/Manage/ManageCustomFormatsModalRow.tsx#ManageCustomFormatsModalRow`
8. `ts|frontend/src/Settings/CustomFormats/CustomFormats/EditCustomFormatModalContent.tsx#EditCustomFormatModalContent`
9. `ts|frontend/src/Settings/CustomFormats/CustomFormats/Specifications/Specification.tsx#Specification`
10. `ts|frontend/src/Settings/CustomFormats/CustomFormats/ExportCustomFormatModalContent.tsx#ExportCustomFormatModalContent`
11. `ts|frontend/src/Settings/useProviderSettings.ts#useDeleteProvider`
12. `ts|frontend/src/Settings/CustomFormats/CustomFormats/useCustomFormats.ts#useBulkDeleteCustomFormats`
13. `ts|frontend/src/Settings/CustomFormats/CustomFormats/Manage/Edit/ManageCustomFormatsEditModalContent.tsx#ManageCustomFormatsEditModalContent`
14. `ts|frontend/src/Settings/CustomFormats/CustomFormats/useCustomFormats.ts#useBulkEditCustomFormats`
15. `ts|frontend/src/Settings/CustomFormats/CustomFormats/Specifications/AddSpecificationModalContent.tsx#AddSpecificationModalContent`
16. `ts|frontend/src/Settings/CustomFormats/CustomFormats/useCustomFormats.ts#useManageCustomFormat`
17. `ts|frontend/src/Settings/CustomFormats/CustomFormats/ImportCustomFormatModalContent.tsx#ImportCustomFormatModalContent`
18. `ts|frontend/src/Settings/CustomFormats/CustomFormats/useCustomFormats.ts#getNextSpecId`
19. `ts|frontend/src/Settings/CustomFormats/CustomFormats/ExportCustomFormatModalContent.tsx#replacer`
20. `ts|frontend/src/Settings/useProviderSettings.ts#useManageProviderSettings`
21. [`endpoint:Sonarr.Api.V3|PUT /api/v3/CustomFormat/bulk`](../backend/sonarr.api.v3-c45bd0/put-api-v3-customformat-bulk-6f3c45.md)
22. [`endpoint:Sonarr.Api.V3|DELETE /api/v3/CustomFormat/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-customformat-bulk-b09d94.md)
23. `ts|frontend/src/Helpers/Hooks/useApiMutation.ts#useApiMutation`

<sub>commit 0bffefe · inputs a1000fc · slice of 308 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

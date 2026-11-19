# Tag Settings

`page:/settings/tags` · screen · tsconfig.json

From the code: /settings/tags · TagSettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled Tags under Settings and has two sections: Tags and Auto Tagging, each with a heading and a description. [1]
- **S2** The Tags section lists tags as cards sorted by label, each card showing the tag label. [2][3][4]
- **S3** When there are no tags, the Tags section shows an info alert "NoTagsHaveBeenAddedYet" instead of cards. [2]
- **S4** The Tags section shows a load error message (TagsLoadError) if tags or tag details fail to load, and a loading state while fetching. [2]
- **S5** Each tag card shows labels with counts of what uses the tag: series, delay profiles, import lists, connections, release profiles, indexers, download clients and auto tagging. Counts of zero are hidden. [4][5]
- **S6** A tag card that is not used by anything shows "NoLinks". [4]
- **S7** The tag details modal lists the series (sorted by sort title), delay profiles, connections, import lists, release profiles (required and ignored terms), indexers, download clients and auto tags that use the tag. Sections with no items are hidden. [6][7]
- **S8** The tag details modal shows a message that the tag is not used and can be deleted when nothing uses it. [6]
- **S9** Delay profiles in the details modal show preferred protocol, and usenet and torrent delay times or a disabled message. [8]
- **S10** The Auto Tagging section lists auto tagging cards sorted by name, plus an "AddAutoTag" add card. It shows a load error message (AutoTaggingLoadError) on failure. [9][10]
- **S11** Each auto tagging card shows its name, a conditions count, its tags when it has any, and a label per condition. Required conditions are marked success, negated ones danger. [11]
- **S12** The auto tagging edit modal has Name, Remove Tags Automatically and Tags fields and a Conditions list with an Add Condition card. Its title is Add Auto Tag or Edit Auto Tag. [12]
- **S13** Each condition card shows its name, implementation name, and Negated and Required labels when set. [13]
- **S14** The Add Condition modal lists available condition types, with a loading indicator and an error alert if the list fails to load. [14]
- **S15** The condition edit modal has Name, the condition's own fields, Negate and Required checkboxes. For fields labelled Regular Expression it shows an info alert with regex help links. [15]

## Actions

- **A1** Clicking a tag card opens its details modal. [4]
- **A2** Delete in the tag details modal closes it and opens a confirmation (Delete Tag); confirming deletes the tag via the API. [4][16]
- **A3** The page loads tags and tag details from the API. [17][18]
- **A4** Clicking the add card opens an empty Add Auto Tag modal; clicking a card opens it for editing; the clone icon opens the modal prefilled from that auto tag. [9][11]
- **A5** Save in the auto tagging modal saves it, and the modal closes after a successful save. [12][19]
- **A6** Delete in the edit modal (existing auto tags only) closes it and opens a confirmation; confirming deletes the auto tag. [11][12][20]
- **A7** Add Condition opens a type picker; choosing a type (or preset) opens the condition edit modal, and saving adds the condition to the auto tag. [12][14]
- **A8** Conditions can be edited, cloned or deleted (with confirmation). [13][19]

## Rules

- **R1** A tag's Delete button in the details modal is disabled, with the tooltip "TagCannotBeDeletedWhileInUse", while the tag is used by anything. [6]
- **R2** A tag counts as used if any of series, delay profiles, import lists, connections, release profiles, indexers, download clients or auto tags reference it. [4]
- **R3** Release profile usage merges restriction and excluded release profile ids without duplicates. [4]
- **R4** A cloned auto tag gets a default name based on the source name and id 0. [19]
- **R5** A new condition gets an id one higher than the current maximum, or 1 if there are none; a cloned condition gets a copied-name default. [21][19]
- **R6** The Delete button on a condition appears only when the condition already has an id. [15]
- **R7** Existing providers saved with no changes skip testing and validation; saving the same settings again skips testing. [22]

## Side effects

- **E1** Confirming tag deletion sends a DELETE to the tag API and updates the cached tag list. [16]
- **E2** Confirming auto tag deletion sends a DELETE to the API and removes it from the cached list. [23]
- **E3** Opening the page also loads delay profiles, release profiles, connections, indexers, import lists and download clients, and invalidates the cached release profiles. [2]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:403` HTTP DELETE to a URL built at runtime
- the depth limit left out 56 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/Tags/TagSettings.tsx#TagSettings`
2. `ts|frontend/src/Settings/Tags/Tags.tsx#Tags`
3. `ts|frontend/src/Tags/useTags.ts#useSortedTagList`
4. `ts|frontend/src/Settings/Tags/Tag.tsx#Tag`
5. `ts|frontend/src/Settings/Tags/TagInUse.tsx#TagInUse`
6. `ts|frontend/src/Settings/Tags/Details/TagDetailsModalContent.tsx#TagDetailsModalContent`
7. `ts|frontend/src/Settings/Tags/Details/TagDetailsModalContent.tsx#useMatchingSeries`
8. `ts|frontend/src/Settings/Tags/Details/TagDetailsDelayProfile.tsx#TagDetailsDelayProfile`
9. `ts|frontend/src/Settings/Tags/AutoTagging/AutoTaggings.tsx#AutoTaggings`
10. `ts|frontend/src/Settings/Tags/AutoTagging/useAutoTaggings.ts#useSortedAutoTaggings`
11. `ts|frontend/src/Settings/Tags/AutoTagging/AutoTagging.tsx#AutoTagging`
12. `ts|frontend/src/Settings/Tags/AutoTagging/EditAutoTaggingModalContent.tsx#EditAutoTaggingModalContent`
13. `ts|frontend/src/Settings/Tags/AutoTagging/Specifications/Specification.tsx#Specification`
14. `ts|frontend/src/Settings/Tags/AutoTagging/Specifications/AddSpecificationModalContent.tsx#AddSpecificationModalContent`
15. `ts|frontend/src/Settings/Tags/AutoTagging/Specifications/EditSpecificationModalContent.tsx#EditSpecificationModalContent`
16. `ts|frontend/src/Tags/useTags.ts#useDeleteTag`
17. `ts|frontend/src/Tags/useTags.ts#useTags`
18. `ts|frontend/src/Tags/useTagDetails.ts#useTagDetails`
19. `ts|frontend/src/Settings/Tags/AutoTagging/useAutoTaggings.ts#useManageAutoTagging`
20. `ts|frontend/src/Settings/Tags/AutoTagging/useAutoTaggings.ts#useDeleteAutoTagging`
21. `ts|frontend/src/Settings/Tags/AutoTagging/useAutoTaggings.ts#getNextSpecId`
22. `ts|frontend/src/Settings/useProviderSettings.ts#useManageProviderSettings`
23. `ts|frontend/src/Settings/useProviderSettings.ts#useDeleteProvider`

<sub>commit 0bffefe · inputs 6a9811f · slice of 320 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

# Profiles settings

`page:/settings/profiles` · screen · tsconfig.json

From the code: /settings/profiles · Profiles

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page titled Profiles has three sections: Quality Profiles, Delay Profiles and Release Profiles, each with a heading and description. [1]
- **S2** Each section shows a load-error message if loading fails: 'QualityProfilesLoadError', 'DelayProfilesLoadError' or 'ReleaseProfilesLoadError' (translation keys). [2][3][4]
- **S3** Quality profiles appear as cards sorted by name, followed by an 'Add Quality Profile' card. [2]
- **S4** Each quality profile card shows its name, whether upgrades are on or off, the cutoff quality name when set, and chips for allowed qualities. Groups show a count and a tooltip listing their qualities. [5]
- **S5** The cutoff quality chip is highlighted; when upgrades are allowed it has a title explaining upgrades continue until the quality is met or exceeded. [5]
- **S6** A quality profile card with many allowed qualities shows only the first CHIP_CAP_THRESHOLD chips plus a 'show more' count button. [5]
- **S7** The Delay Profiles list has column headers Tags, Preferred Protocol, Usenet Delay and Torrent Delay. Profiles are ordered by their order value, with the default profile (id 1) shown last, followed by an 'Add Delay Profile' link. [3][6]
- **S8** Each delay profile row shows a Default label for id 1, its tags (or 'Any' when none), the preferred protocol (only torrent, only usenet, or prefer one), and usenet and torrent delays. [7]
- **S9** A delay shows '-' when that protocol is disabled, 'No delay' when zero, 'one minute' when 1, otherwise the number of minutes. [8]
- **S10** Release profiles appear as cards followed by an 'Add Release Profile' card. [4]
- **S11** Each release profile card shows its name ('Unnamed' if blank), Enabled or Disabled status, required terms (success style) and ignored terms (danger style), tags, excluded tags, and the names of selected indexers. [9]

## Actions

- **A1** Click 'Add Quality Profile' to open an empty add modal; a card's clone action opens the modal pre-filled from that profile. [2][5]
- **A2** Click a quality profile card to edit it; the edit modal has Name, Upgrades Allowed, Upgrade Until, custom format score fields, a quality list with Qualities/Groups/Sizes modes, and Delete, Cancel and Save buttons. [5][10][11]
- **A3** In the quality profile modal, qualities can be allowed or disallowed, grouped, ungrouped, renamed (groups), reordered by drag and drop, and sized; custom format scores can be changed. [10][12][13]
- **A4** Delete a quality profile from its edit modal, then confirm in the 'Delete Quality Profile' dialog. [5][14]
- **A5** Click 'Add Delay Profile' or a delay profile row to open the add/edit modal; non-default rows have a delete button that opens a confirmation. [3][7]
- **A6** Drag non-default delay profiles to reorder them; the new position is sent to the server. [3][15]
- **A7** Click 'Add Release Profile' or a release profile card to open the add/edit modal; a delete action on the card opens a confirmation. [4][9]
- **A8** The release profile modal edits Name, Enable Profile, Must Contain, Must Not Contain, Air Date Restriction, grace period, season pack option, Indexer, Tags and Excluded Tags. [16]

## Rules

- **R1** Deleting a quality profile is disabled while it is used by any series or import list; a popover shows the usage counts. [10][17]
- **R2** The Upgrade Until field shows only when Upgrades Allowed is checked. Minimum custom format score appears only when the profile has custom formats; the upgrade score fields need both upgrades allowed and custom formats. [10]
- **R3** If the cutoff is unset or no longer allowed, it is set to the first allowed quality. [10]
- **R4** The minimum upgrade format score input has a minimum of 1. [10]
- **R5** A cloned quality profile gets a copied name, and a new profile starts from the server schema. [18]
- **R6** The default delay profile (id 1) cannot be dragged or deleted, and its tags are not editable. [7][19]
- **R7** Delay profile protocol options set enabled flags and preferred protocol; delay fields show only for enabled protocols, in minutes. [19]
- **R8** The minimum custom format score field shows only when 'bypass if above custom format score' is checked. [19]
- **R9** In release profiles, the air date grace period (hours) shows only when Air Date Restriction is checked, and Delete shows only when editing an existing profile. [16]
- **R10** A modal closes automatically after a save finishes without error. [10][19][16]
- **R11** Saving an existing profile with no changes skips testing and validation. [20]

## Side effects

- **E1** Saving a profile sends it to the server and clears pending edits on success. [20]
- **E2** Confirming a delete sends an HTTP DELETE for the profile and removes it from the cached list on success. [14]
- **E3** Reordering a delay profile sends an HTTP PUT to reorder/{id}, with an optional 'after' parameter, and replaces the cached list with the response. [15]

## Couldn't see

- ❓ `frontend/src/Settings/Profiles/Delay/useDelayProfiles.ts:121` HTTP PUT to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:403` HTTP DELETE to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime
- the depth limit left out 71 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/Profiles/Profiles.tsx#Profiles`
2. `ts|frontend/src/Settings/Profiles/Quality/QualityProfiles.tsx#QualityProfiles`
3. `ts|frontend/src/Settings/Profiles/Delay/DelayProfiles.tsx#DelayProfiles`
4. `ts|frontend/src/Settings/Profiles/Release/ReleaseProfiles.tsx#ReleaseProfiles`
5. `ts|frontend/src/Settings/Profiles/Quality/QualityProfile.tsx#QualityProfile`
6. `ts|frontend/src/Settings/Profiles/Delay/useDelayProfiles.ts#useSortedDelayProfiles`
7. `ts|frontend/src/Settings/Profiles/Delay/DelayProfile.tsx#DelayProfile`
8. `ts|frontend/src/Settings/Profiles/Delay/DelayProfile.tsx#getDelay`
9. `ts|frontend/src/Settings/Profiles/Release/ReleaseProfileItem.tsx#ReleaseProfileItem`
10. `ts|frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContent.tsx#EditQualityProfileModalContent`
11. `ts|frontend/src/Settings/Profiles/Quality/QualityProfileItems.tsx#QualityProfileItems`
12. `ts|frontend/src/Settings/Profiles/Quality/useQualityProfileDnd.ts#useQualityProfileDnd`
13. `ts|frontend/src/Settings/Profiles/Quality/QualityProfileFormatItem.tsx#QualityProfileFormatItem`
14. `ts|frontend/src/Settings/useProviderSettings.ts#useDeleteProvider`
15. `ts|frontend/src/Settings/Profiles/Delay/useDelayProfiles.ts#useReorderDelayProfile`
16. `ts|frontend/src/Settings/Profiles/Release/EditReleaseProfileModalContent.tsx#EditReleaseProfileModalContent`
17. `ts|frontend/src/Settings/Profiles/Quality/useQualityProfileInUse.ts#useQualityProfileInUse`
18. `ts|frontend/src/Settings/Profiles/Quality/useQualityProfiles.ts#useManageQualityProfile`
19. `ts|frontend/src/Settings/Profiles/Delay/EditDelayProfileModalContent.tsx#EditDelayProfileModalContent`
20. `ts|frontend/src/Settings/useProviderSettings.ts#useManageProviderSettings`

<sub>commit 0bffefe · inputs 79f0a15 · slice of 442 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

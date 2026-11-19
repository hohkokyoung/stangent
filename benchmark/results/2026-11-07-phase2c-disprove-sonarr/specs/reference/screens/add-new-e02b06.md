# Add New Series

`page:/add/new` · screen · tsconfig.json

From the code: /add/new · AddNewSeries

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Add New Series" with a "Media" scope and a "Metadata Settings" button in the heading. [1]
- **S2** A search box (placeholder "eg. Breaking Bad, tvdb:####") is shown and focused automatically. A clear button appears only when the box has text. [1]
- **S3** A loading indicator is shown while a search is in progress. [1]
- **S4** When the lookup fails and nothing is loading, an error title and the error message from the response are shown. [1][2]
- **S5** When results exist, a count label (singular for one result, plural otherwise) is shown above the list of results. [1]
- **S6** When a search term returns no results, a "could not find" message with the term, a hint to search by TVDB ID, and a "Why can't I find my show?" link to the wiki are shown. [1]
- **S7** When the search box is empty, a help text and TVDB ID hint are shown. If the library has no series, a "no series have been added" message and an Import Existing Series button linking to /add/import also appear. [1][3]
- **S8** Each result shows the poster (hidden on small screens), title with year in brackets (year hidden if already in the title), overview, and a TVDB external link. [4]
- **S9** The result title and overview use the translation in the preferred metadata language when one exists; otherwise the title falls back to the original title, then the lookup title. [4]
- **S10** Each result shows chips: heart rating percentage with vote count tooltip (only if votes > 0), status, network, original language, genres, and season count. [4][5]
- **S11** Status chip labels are Continuing, Upcoming, Ended and Deleted; other statuses show no status chip. [6]
- **S12** Only the first genre is shown; any further genres appear in a tooltip. [7]
- **S13** A result already in the library shows an "Already in your library" check icon. An excluded result shows a warning icon "Series in import list exclusions". [4]
- **S14** Each result shows a metadata attribution link, "Metadata provided by TheTVDB", pointing to /settings/metadatasource. [8]
- **S15** The add-series dialog shows the title, poster, overview, and fields for Root Folder, Tags, Monitor, Season Folder, Quality Profile, Series Type, Language and Season Type, plus Search checkboxes for Missing and Cutoff Unmet. [9]
- **S16** Info popovers next to Monitor and Series Type explain the options; the Series Type popover describes Anime, Daily and Standard. [9][10]
- **S17** The Metadata Settings dialog has a Preferred Metadata Language select with help text, and Cancel and Save buttons. A save error shows a danger alert. [11]

## Actions

- **A1** Typing in the search box looks up series by the term, with a 300 ms debounce; clearing the box applies immediately. [1][12] → [`endpoint:Sonarr.Api.V3|GET /api/v3/series/lookup`](../backend/sonarr.api.v3-c45bd0/get-api-v3-series-lookup-ead202.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/series/lookup`](../backend/sonarr.api.v5-c79e7b/get-api-v5-series-lookup-3f791d.md)
- **A2** The clear button empties the search box, stops the fetching state and refocuses the input. [1]
- **A3** The Metadata Settings button opens a dialog to choose the preferred metadata language; Save stores it and closes the dialog. [1][11][13]
- **A4** Pressing a result that is not in the library opens the add-series dialog; pressing one already in the library links to its series page. [4]
- **A5** The add button in the dialog, labelled "Add" with the title, submits the new series with the chosen root folder, monitor option, search options, quality profile, series type, season type, language, season folder and tags. [9][14] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/Series`](../backend/sonarr.api.v3-c45bd0/any-api-v3-series-8b55c2.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/Series`](../backend/sonarr.api.v5-c79e7b/any-api-v5-series-29b423.md)

## Rules

- **R1** The lookup runs only when the search term is non-empty, and sends the term and the preferred metadata language id. It does not refetch on window focus. [15]
- **R2** The search term can be prefilled from the "term" query parameter in the URL. [1][16]
- **R3** A result counts as existing when a series in the library has the same TVDB id. The add dialog cannot open for an existing series. [17][4]
- **R4** The preferred metadata language falls back to a default language when the setting or language list is unavailable. [18]
- **R5** In the add dialog, the season type defaults to "official" if available, otherwise the first season type, otherwise "official". [9]
- **R6** The initial series type is the saved default when the lookup series type is standard; otherwise it is the lookup series type. [9]
- **R7** Validation errors and warnings from a failed add (HTTP 400) are mapped onto the matching form fields by property name. [19][20][21]

## Side effects

- **E1** Adding a series sends a POST to /series and, on success, adds or updates that series in the cached series list. [14][22]
- **E2** Saving metadata settings sends a PUT with the existing settings plus the chosen preferred metadata language, updates the cached settings and then closes the dialog. [13][23]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- the depth limit left out 20 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/AddSeries/AddNewSeries/AddNewSeries.tsx#AddNewSeries`
2. `ts|frontend/src/Utilities/Object/getErrorMessage.ts#getErrorMessage`
3. `ts|frontend/src/Series/useSeries.ts#useHasSeries`
4. `ts|frontend/src/AddSeries/AddNewSeries/AddNewSeriesSearchResult.tsx#AddNewSeriesSearchResult`
5. `ts|frontend/src/Components/HeartRating.tsx#HeartRating`
6. `ts|frontend/src/AddSeries/AddNewSeries/AddNewSeriesSearchResult.tsx#getStatusChip`
7. `ts|frontend/src/Series/SeriesGenres.tsx#SeriesGenres`
8. `ts|frontend/src/Components/MetadataAttribution.tsx#MetadataAttribution`
9. `ts|frontend/src/AddSeries/AddNewSeries/AddNewSeriesModalContent.tsx#AddNewSeriesModalContent`
10. `ts|frontend/src/AddSeries/SeriesTypePopoverContent.tsx#SeriesTypePopoverContent`
11. `ts|frontend/src/Settings/MetadataSource/MetadataSettingsModalContent.tsx#MetadataSettingsModalContent`
12. `ts|frontend/src/Helpers/Hooks/useDebounce.ts#useDebounce`
13. `ts|frontend/src/Settings/MetadataSource/useMetadataSourceSettings.ts#useSaveMetadataSourceSettings`
14. `ts|frontend/src/AddSeries/AddNewSeries/useAddSeries.ts#useAddSeries`
15. `ts|frontend/src/AddSeries/AddNewSeries/useAddSeries.ts#useLookupSeries`
16. `ts|frontend/src/Helpers/Hooks/useQueryParams.ts#useQueryParams`
17. `ts|frontend/src/Series/useExistingSeries.ts#useExistingSeries`
18. `ts|frontend/src/Language/useLanguages.ts#usePreferredMetadataLanguage`
19. `ts|frontend/src/Utilities/selectSettings.ts#selectSettings`
20. `ts|frontend/src/Utilities/selectSettings.ts#getFailures`
21. `ts|frontend/src/Utilities/selectSettings.ts#getValidationFailures`
22. `ts|frontend/src/Helpers/Hooks/useApiMutation.ts#addOrUpdateQueryClientItem`
23. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`

<sub>commit 0bffefe · inputs 24d2f3a · slice of 394 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

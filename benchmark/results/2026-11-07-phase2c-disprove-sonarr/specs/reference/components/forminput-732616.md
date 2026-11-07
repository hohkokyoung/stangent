# Form Input (generic form field)

`ts|frontend/src/Components/Form/FormInput.tsx#FormInput` · component · sonarr

From the code: FormInput

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Renders one form field whose input control is chosen by the field's type, with an optional unit label next to it. [1]
- **S2** Any buttons passed in are shown after the input, and the last button is told it is the last one. [1]
- **S3** Validation errors and warnings are listed below the input as help text, combining client-side messages with server-supplied ones. Errors come first in order of client then server. [1]
- **S4** Error and warning entries with an errorMessage show the message with an info link and a detailed-description tooltip; other entries show just their message. [1]
- **S5** The select input shows a loading indicator in place of the dropdown arrow while its options are being fetched. [2]
- **S6** The root folder select lists each root folder path with its free space, flags missing values as 'Missing', and ends with an 'Add a new path' option. [3][4]
- **S7** The UMask input shows the umask, folder and file permission values in octal, plus a rwx-style permission string. [5][6]
- **S8** The key/value list input shows each pair as a key box and a value box, with a remove button on every row except the always-present empty new row at the end. [7][8]
- **S9** The key/value placeholders default to 'Key' and 'Value'. [8]

## Actions

- **A1** Choosing the 'Add a new path' option in the root folder select opens a file browser modal; confirming a path adds it as a root folder. [9][10] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/RootFolder`](../backend/sonarr.api.v3-c45bd0/any-api-v3-rootfolder-081795.md)
- **A2** Typing a new tag name in the series tag input creates the tag if no tag with that label exists, then adds it to the value. [11] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/Tag`](../backend/sonarr.api.v3-c45bd0/any-api-v3-tag-a2227b.md)
- **A3** The OAuth input button starts the OAuth flow; on success it sets each returned result key as a field value. [12][13]
- **A4** The captcha input's refresh button checks the captcha, and solving the reCAPTCHA fetches a captcha cookie token that is put in the field. [14][15]
- **A5** The device input's refresh button reloads the list of devices from the provider. [16]
- **A6** The select input can be opened by click or keyboard; arrow keys move, Enter or Tab selects, Escape closes, and typing letters jumps to a matching option. [2]
- **A7** Clicking a tag in a tag input removes it, and Backspace on empty text removes the last tag. [17][18]

## Rules

- **R1** The error state takes priority: the warning state applies only when there are no errors. [1]
- **R2** The field id defaults to the surrounding form row's id, otherwise to the field name. [1]
- **R3** Number inputs clamp to min and max when the field loses focus, and an empty value becomes null; float inputs parse as decimals, others as integers. [19][20][21]
- **R4** The password input is a text input of type password with copy blocked. [22][23]
- **R5** Quality profile options are sorted by name; if the value is empty or not among the options, the first numeric option is selected automatically. [24][25]
- **R6** Root folders are sorted by path, and an empty root folder value defaults to the first available folder. [3][9]
- **R7** Download client options are filtered to the given protocol (default torrent), sorted by name, with an optional '(Any)' entry with key 0. [26]
- **R8** Indexer flags are stored as a sum of the selected flag ids. [27]
- **R9** Several select inputs can add a 'No Change' and a disabled '(Mixed)' entry at the top of their options. [28][29][30]
- **R10** Language options exclude Any, Original and Unknown unless the matching include flag is set; Any is included by default. [30]
- **R11** Text tag input splits entered text on commas and trims it, except entries starting with '/' which are kept whole; delimiters are Tab, Enter and comma. [31]
- **R12** The key/value input drops rows where both key and value are empty when focus leaves. [7]
- **R13** Autocomplete suggestions are values that contain the typed text, ignoring case. [32]

## Side effects

- **E1** Adding a root folder from the select posts it to the root folder API and updates the cached list. [33]
- **E2** Creating a tag posts it to the tag API and updates the cached tag list. [34]
- **E3** Provider option selects load their options with a POST to the provider's action endpoint. [35][36]
- **E4** The OAuth flow opens a browser window and calls provider actions startOAuth, continueOAuth, pollOAuth and getOAuthToken. [37][13][38]
- **E5** OAuth failures are pushed to the field's error list. [12]
- **E6** Tag creation failures are pushed to the field's error and warning list. [11]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/OAuth/useOAuth.ts:120` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Utilities/requestAction.js:37` HTTP GET to a URL built at runtime
- the depth limit left out 17 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Components/Form/FormInput.tsx#FormInput`
2. `ts|frontend/src/Components/Form/Select/EnhancedSelectInput.tsx#EnhancedSelectInput`
3. `ts|frontend/src/Components/Form/Select/RootFolderSelectInput.tsx#useRootFolderOptions`
4. `ts|frontend/src/Components/Form/Select/RootFolderSelectInputOption.tsx#RootFolderSelectInputOption`
5. `ts|frontend/src/Components/Form/Select/UMaskInput.tsx#UMaskInput`
6. `ts|frontend/src/Components/Form/Select/UMaskInput.tsx#formatPermissions`
7. `ts|frontend/src/Components/Form/KeyValueListInput.tsx#KeyValueListInput`
8. `ts|frontend/src/Components/Form/KeyValueListInputItem.tsx#KeyValueListInputItem`
9. `ts|frontend/src/Components/Form/Select/RootFolderSelectInput.tsx#RootFolderSelectInput`
10. `ts|frontend/src/Components/FileBrowser/FileBrowserModalContent.tsx#FileBrowserModalContent`
11. `ts|frontend/src/Components/Form/Tag/SeriesTagInput.tsx#SeriesTagInput`
12. `ts|frontend/src/Components/Form/OAuthInput.tsx#OAuthInput`
13. `ts|frontend/src/OAuth/useOAuth.ts#useOAuth`
14. `ts|frontend/src/Components/Form/CaptchaInput.tsx#CaptchaInput`
15. `ts|frontend/src/Components/Form/useCaptcha.ts#useCaptcha`
16. `ts|frontend/src/Components/Form/Tag/DeviceInput.tsx#DeviceInput`
17. `ts|frontend/src/Components/Form/Tag/TagInputTag.tsx#TagInputTag`
18. `ts|frontend/src/Components/Form/Tag/TagInput.tsx#TagInput`
19. `ts|frontend/src/Components/Form/NumberInput.tsx#NumberInput`
20. `ts|frontend/src/Components/Form/NumberInput.tsx#parseValue`
21. `ts|frontend/src/Components/Form/FloatInput.tsx#FloatInput`
22. `ts|frontend/src/Components/Form/PasswordInput.tsx#PasswordInput`
23. `ts|frontend/src/Components/Form/PasswordInput.tsx#onCopy`
24. `ts|frontend/src/Components/Form/Select/QualityProfileSelectInput.tsx#QualityProfileSelectInput`
25. `ts|frontend/src/Components/Form/Select/QualityProfileSelectInput.tsx#useValues`
26. `ts|frontend/src/Components/Form/Select/DownloadClientSelectInput.tsx#DownloadClientSelectInput`
27. `ts|frontend/src/Components/Form/Select/IndexerFlagsSelectInput.tsx#IndexerFlagsSelectInput`
28. `ts|frontend/src/Components/Form/Select/MonitorEpisodesSelectInput.tsx#MonitorEpisodesSelectInput`
29. `ts|frontend/src/Components/Form/Select/SeriesTypeSelectInput.tsx#SeriesTypeSelectInput`
30. `ts|frontend/src/Components/Form/Select/LanguageSelectInput.tsx#LanguageSelectInput`
31. `ts|frontend/src/Components/Form/Tag/TextTagInput.tsx#TextTagInput`
32. `ts|frontend/src/Components/Form/AutoCompleteInput.tsx#AutoCompleteInput`
33. `ts|frontend/src/RootFolder/useRootFolders.ts#useAddRootFolder`
34. `ts|frontend/src/Tags/useTags.ts#useAddTag`
35. `ts|frontend/src/Settings/useProviderOptions.ts#useProviderOptions`
36. `ts|frontend/src/Components/Form/Select/ProviderOptionSelectInput.tsx#ProviderOptionSelectInput`
37. `ts|frontend/src/OAuth/useOAuth.ts#showOAuthWindow`
38. `ts|frontend/src/OAuth/useOAuth.ts#executeIntermediateRequest`

<sub>commit 0bffefe · inputs a9d86f0 · slice of 774 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

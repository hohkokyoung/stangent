# Select Series modal

`ts|frontend/src/InteractiveImport/Series/SelectSeriesModal.tsx#SelectSeriesModal` · component · sonarr

From the code: SelectSeriesModal

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** A modal dialog whose header reads "<modalTitle> - Select Series". [1]
- **S2** A filter text box with focus on open and a placeholder from the translation key 'FilterSeriesPlaceholder'. [1][2]
- **S3** A table of series with a header row; only columns marked visible are shown in the header. [3]
- **S4** Each series row shows title, year, TVDB id as a label, and IMDb id as a label (the IMDb label is omitted when there is no IMDb id). [4]
- **S5** The list of series comes from the series query and is empty while no data has loaded. [5][1]
- **S6** A footer has a Cancel button. [1]

## Actions

- **A1** Typing in the filter box narrows the list of series. [1]
- **A2** Pressing a series row selects that series and passes the full series to the onSeriesSelect callback. [6][1][7]
- **A3** Pressing Cancel closes the modal. [1][8]

## Rules

- **R1** Series are sorted by sortTitle using numeric-aware string comparison. [1][9]
- **R2** The filter is case-insensitive and matches series whose title contains the text, or whose TVDB id or IMDb id contains it. [1]
- **R3** A row selection only fires when the series has a truthy id. [6]
- **R4** Series data is fetched from '/series' and considered fresh for 5 minutes. [5]
- **R5** The list is virtualized with a fixed row height of 38. [1]

## Side effects

- **E1** Selecting a series calls the parent's onSeriesSelect callback; closing calls onModalClose. The modal itself saves nothing. [1][10]
- **E2** Opening the modal loads the series list from '/series'. [5]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/InteractiveImport/Series/SelectSeriesModalContent.tsx#SelectSeriesModalContent`
2. `ts|frontend/src/Utilities/String/translate.ts#translate`
3. `ts|frontend/src/InteractiveImport/Series/SelectSeriesModalTableHeader.tsx#SelectSeriesModalTableHeader`
4. `ts|frontend/src/InteractiveImport/Series/SelectSeriesRow.tsx#SelectSeriesRow`
5. `ts|frontend/src/Series/useSeries.ts#useSeries`
6. `ts|frontend/src/InteractiveImport/Series/SelectSeriesModalContent.tsx#Row`
7. `ts|frontend/src/InteractiveImport/Series/SelectSeriesModal.tsx#SelectSeriesModalProps.onSeriesSelect`
8. `ts|frontend/src/InteractiveImport/Series/SelectSeriesModal.tsx#SelectSeriesModalProps.onModalClose`
9. `ts|frontend/src/Utilities/Array/sortByProp.ts#sortByProp`
10. `ts|frontend/src/InteractiveImport/Series/SelectSeriesModal.tsx#SelectSeriesModal`

<sub>commit 0bffefe · inputs 6a3afc4 · slice of 111 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

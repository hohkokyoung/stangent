# Queue item status details

`ts|frontend/src/Activity/Queue/QueueDetails.tsx#QueueDetails` · component · sonarr

From the code: QueueDetails

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** While an item is downloading or paused with no warning or error, and progress is under 5%, it shows a downloading icon whose tooltip reads "<state> - <progress>% <title>" (progress to one decimal). [1][2]
- **S2** While downloading or paused with no warning or error, and progress is 5% or more, it shows the progress bar anchor in a popover. The popover title is "<state> - <progress>%" and its body is the item title. [1][3]
- **S3** In all other cases it shows the queue status component, given the title, status, tracked download status and state, status messages and error message. [1][4]
- **S4** The state text is "Paused" when the status is paused, otherwise "Downloading"; both are translated. [1][5]

## Actions

Nothing found in the code.

## Rules

- **R1** Progress is computed as 100 - (sizeLeft / size) * 100. [1]
- **R2** The progress view is used only when status is downloading or paused and tracked download status is neither warning nor error. [1]
- **R3** Tracked download state defaults to 'downloading' and tracked download status defaults to 'ok' when not supplied. [1]
- **R4** The popover is positioned bottom-start; the fallback status tooltip is positioned left. [1]
- **R5** Translation falls back to the key if no translation exists, and replaces {token} placeholders, with appName set to 'Sonarr'. [5]

## Side effects

- **E1** In non-production builds, a missing translation key logs a console warning. [5]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Activity/Queue/QueueDetails.tsx#QueueDetails`
2. [`ts|frontend/src/Components/Icon.tsx#Icon`](../components/icon-4b36a8.md)
3. [`ts|frontend/src/Components/Tooltip/Popover.tsx#Popover`](../components/popover-f2b153.md)
4. [`ts|frontend/src/Activity/Queue/QueueStatus.tsx#QueueStatus`](../components/queuestatus-794914.md)
5. `ts|frontend/src/Utilities/String/translate.ts#translate`

<sub>commit 0bffefe · inputs 50134d5 · slice of 37 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

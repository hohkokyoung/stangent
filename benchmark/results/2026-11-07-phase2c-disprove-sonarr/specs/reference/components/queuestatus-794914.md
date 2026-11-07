# Queue Status Icon

`ts|frontend/src/Activity/Queue/QueueStatus.tsx#QueueStatus` · component · sonarr

From the code: QueueStatus

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Shows a status icon for a queue item, with a popover that opens to give a title and body. [1][2][3]
- **S2** By default the title is 'Downloading' with a downloading icon in the default kind. [1][4]
- **S3** Status 'paused' shows the paused icon with title 'Paused'; 'queued' shows the queued icon with 'Queued'. [1]
- **S4** Status 'completed' shows the downloaded icon with title 'Downloaded'. [1]
- **S5** For a completed item, the title gets a suffix by tracked download state: importBlocked adds 'Unable to import automatically' (warning), importPending adds 'Waiting to import' (purple), importing adds 'Importing' (primary), failedPending adds 'Waiting to process' (danger). [1]
- **S6** Status 'delay' shows the pending icon with title 'Pending'. Status 'downloadClientUnavailable' shows the pending icon in warning kind with title 'Pending download client unavailable'. [1]
- **S7** Status 'failed' shows the downloading icon in danger kind with title 'Download failed'. [1]
- **S8** Status 'warning' shows the downloading icon in warning kind. The title is a download warning built from the error message, or from 'Check download client for details' when there is no error message. [1]
- **S9** When the tracked download status is error, a completed item shows the download icon in danger kind with an 'Import failed' title that includes the source title. Other statuses show the downloading icon in danger kind with 'Download failed'. [1]
- **S10** When the tracked download status is warning or error, the popover body lists each status message group's title with its messages as a bulleted list. Otherwise the body is the source title. [1][5]
- **S11** The title is also used as the accessible label of the popover. [1]

## Actions

Nothing found in the code.

## Rules

- **R1** Tracked download status defaults to 'ok', tracked download state defaults to 'downloading', status messages default to an empty list, and canFlip defaults to false. [1]
- **R2** A tracked download status of 'warning' sets the icon kind to warning, overriding any state-based kind. [1]
- **R3** A status message group with no messages is given a distinct 'no messages' style. [5]
- **R4** Titles are translated; a missing key falls back to the key itself, and {token} placeholders are replaced from the supplied values. [4]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Activity/Queue/QueueStatus.tsx#QueueStatus`
2. [`ts|frontend/src/Components/Tooltip/Popover.tsx#Popover`](../components/popover-f2b153.md)
3. [`ts|frontend/src/Components/Icon.tsx#Icon`](../components/icon-4b36a8.md)
4. `ts|frontend/src/Utilities/String/translate.ts#translate`
5. `ts|frontend/src/Activity/Queue/QueueStatus.tsx#getDetailedPopoverBody`

<sub>commit 0bffefe · inputs b7ed406 · slice of 44 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

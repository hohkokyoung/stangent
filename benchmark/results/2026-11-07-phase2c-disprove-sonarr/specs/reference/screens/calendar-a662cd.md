# Calendar

`page:/calendar` · screen · tsconfig.json

From the code: /calendar · CalendarPage

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Calendar" and has a toolbar with iCal link, RSS sync, search for missing, options and a filter menu. [1]
- **S2** When the library has no series, the calendar is replaced by a message to import or add series, with buttons "Import Existing Series" (/add/import) and "Add New Series" (/add/new). [1][2]
- **S3** The legend is shown below the calendar only when series exist. [1]
- **S4** The filter menu is disabled when there are no series. [1]
- **S5** While calendar data loads, a loading indicator shows; if loading fails, a danger alert shows "CalendarLoadError" text. [3]
- **S6** The calendar header shows previous/next arrows, a Today link, a go-to-date picker, a title and view switchers for Month, Week, Forecast, Day and Agenda; a loading indicator appears while fetching. [4][5]
- **S7** The header title is the long date in Day view, month and year in Month view, "Agenda" in Agenda view, and a start — end date range in other views. [4]
- **S8** On large screens the view choices are in a dropdown menu; the Month option is hidden on small screens. Otherwise each view is a separate button. [4]
- **S9** Non-agenda views show a row of day-of-week headers and a grid of days; in Month view the header shows the first 7 dates. [3][6][7]
- **S10** Agenda view lists episodes in order with a long-date heading whenever the day changes. [8]
- **S11** Each day cell lists that day's episodes; in Day view with none it shows "NoEpisodesAiring" text. Month view cells show the day number. [9]
- **S12** Each event shows series title, episode title and season x episode number (when episode information is on), and air time range; status icons show for warnings, downloading, premiere, finale, special and cutoff not met. [10]
- **S13** Agenda events can also show series cover art when that option is on. [11]
- **S14** An event's status is downloaded, downloading, unmonitored, onAir, missing or unaired. [12]
- **S15** The legend lists Unaired, Unmonitored, On Air, Missing, Downloading, Downloaded and Premiere, plus Season Finale/Series Finale, Special and Cutoff Not Met entries depending on options. [13]

## Actions

- **A1** Pressing the iCal link toolbar item opens a Calendar Feed dialog with options for unmonitored, season premieres only, all-day events and tags, and a generated feed URL with copy and webcal buttons. [1][14]
- **A2** Pressing RSS Sync runs the RssSync command; the button spins while it is queued or started. [1][15][16]
- **A3** Pressing Search for Missing runs an episode search for the missing episode IDs in range. [17]
- **A4** Pressing Options opens the Calendar Options dialog with local options (collapse multiple episodes, cover art, episode information, finale/special/cutoff icons, full color events) and global settings (first day of week, week column header, time format, color impaired mode). [1][18]
- **A5** Changing a global setting in the options dialog saves it immediately as a UI setting. [18][19]
- **A6** Choosing a filter sets the selected calendar filter; custom filters of type calendar are listed. [1][20]
- **A7** Users can go to the previous or next range, jump to today, pick a date, or change the view from the header. [4][21]
- **A8** Swiping horizontally by more than 100 px moves to the previous or next range. [7]
- **A9** Pressing an event opens the episode details dialog. [10][11]
- **A10** A collapsed group of episodes can be expanded and collapsed. [22]
- **A11** The calendar loads episodes for the visible date range. [23] → [`endpoint:Sonarr.Api.V5|GET /api/v5/Calendar`](../backend/sonarr.api.v5-c79e7b/get-api-v5-calendar-143877.md)

## Rules

- **R1** The number of days shown is the page width divided by the minimum day width, clamped between 3 and 7. [1]
- **R2** Missing episodes for search are those with no file, aired within the visible range and already in the past, and not in the download queue. [24]
- **R3** The Search for Missing button is disabled when there are no missing episodes. [17]
- **R4** Repeating the same command within 5 seconds is ignored. [16]
- **R5** The unmonitored and includeSpecials filters default to true; a tags filter limits episodes by tag. These are sent with the date range to the calendar request. [23]
- **R6** Date range: Day view is one day; Week is the week by first day of week; Forecast starts yesterday and spans the day count; Month covers the full weeks of the month; Agenda runs from yesterday to one month ahead. [25]
- **R7** Fetched range is widened by 1 day in Day view and 1 week each side in Week and Forecast views. [26]
- **R8** Previous/next moves by the day count in Forecast view, otherwise by one view unit. Agenda view disables navigation and date picker. [27][28][4]
- **R9** The calendar jumps back to today on a repeating timer, and on opening unless the page was reached by back/forward navigation. [3][29]
- **R10** With collapse multiple episodes on, a day's episodes of the same series and season are grouped, and days are sorted by air time. [30]

## Side effects

- **E1** Pressing RSS Sync or Search for Missing sends a command to the server (POST /command). [16]
- **E2** Changing a global option saves UI settings to the server (PUT). [31][19]
- **E3** The calendar refetches when episode files are updated or deleted, and when a series refresh finishes. [3][32]
- **E4** Episode files for the visible episodes are requested from the episode file endpoint. [33]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- the depth limit left out 25 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Calendar/CalendarPage.tsx#CalendarPage`
2. `ts|frontend/src/Series/NoSeries.tsx#NoSeries`
3. `ts|frontend/src/Calendar/Calendar.tsx#Calendar`
4. `ts|frontend/src/Calendar/Header/CalendarHeader.tsx#CalendarHeader`
5. `ts|frontend/src/Calendar/Header/CalendarHeaderViewButton.tsx#getTitle`
6. `ts|frontend/src/Calendar/Day/DaysOfWeek.tsx#DaysOfWeek`
7. `ts|frontend/src/Calendar/Day/CalendarDays.tsx#CalendarDays`
8. `ts|frontend/src/Calendar/Agenda/Agenda.tsx#Agenda`
9. `ts|frontend/src/Calendar/Day/CalendarDay.tsx#CalendarDay`
10. `ts|frontend/src/Calendar/Events/CalendarEvent.tsx#CalendarEvent`
11. `ts|frontend/src/Calendar/Agenda/AgendaEvent.tsx#AgendaEvent`
12. `ts|frontend/src/Calendar/getStatusStyle.ts#getStatusStyle`
13. `ts|frontend/src/Calendar/Legend/Legend.tsx#Legend`
14. `ts|frontend/src/Calendar/iCal/CalendarLinkModalContent.tsx#CalendarLinkModalContent`
15. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`
16. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
17. `ts|frontend/src/Calendar/CalendarMissingEpisodeSearchButton.tsx#CalendarMissingEpisodeSearchButton`
18. `ts|frontend/src/Calendar/Options/CalendarOptionsModalContent.tsx#CalendarOptionsModalContent`
19. `ts|frontend/src/Settings/UI/useUiSettings.ts#useSaveUiSettings`
20. `ts|frontend/src/Calendar/CalendarFilterModal.tsx#CalendarFilterModal`
21. `ts|frontend/src/Calendar/useCalendar.ts#goToDate`
22. `ts|frontend/src/Calendar/Events/CalendarEventGroup.tsx#CalendarEventGroup`
23. `ts|frontend/src/Calendar/useCalendar.ts#useCalendar`
24. `ts|frontend/src/Calendar/CalendarMissingEpisodeSearchButton.tsx#useMissingEpisodeIdsSelector`
25. `ts|frontend/src/Calendar/useCalendar.ts#getDates`
26. `ts|frontend/src/Calendar/useCalendar.ts#getPopulatableRange`
27. `ts|frontend/src/Calendar/useCalendar.ts#goToPreviousRange`
28. `ts|frontend/src/Calendar/useCalendar.ts#goToNextRange`
29. `ts|frontend/src/Helpers/Hooks/useCurrentPage.ts#useCurrentPage`
30. `ts|frontend/src/Calendar/Day/CalendarDay.tsx#useCalendarEvents`
31. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`
32. `ts|frontend/src/Utilities/pagePopulator.ts#registerPagePopulator`
33. `ts|frontend/src/EpisodeFile/useEpisodeFiles.ts#useEpisodeFiles`

<sub>commit 0bffefe · inputs cc112bb · slice of 556 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

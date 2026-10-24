# Season Pass bulk update

`endpoint:Sonarr.Api.V3|POST /api/v3/SeasonPass` · backend · Sonarr.Api.V3

From the code: POST /api/v3/SeasonPass

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an Accepted response with an empty object body after processing all series. [1]

## Actions

- **A1** Accepts a JSON body listing series and optional monitoring options, and updates monitoring for each listed series. [1][2] → `endpoint:Sonarr.Api.V3|POST /api/v3/SeasonPass`

## Rules

- **R1** A series' monitored flag is changed only when the request supplies a Monitored value for it. [1]
- **R2** When seasons are supplied for a series, each existing season whose number matches gets the supplied monitored value; non-matching seasons are unchanged. [1]
- **R3** If the monitoring option is None, the series is set to not monitored. [1]
- **R4** If no monitoring options are given, the series is saved without changing its episodes. [3]
- **R5** If the monitor type is Skip, nothing further is done and episode monitoring is left as is. [3]
- **R6** If the monitor type is Unknown, the legacy monitoring routine is used. [3]
- **R7** The last season (number above 0) is monitored when the monitor type is All, or Future with a Continuing or Upcoming series. [3]
- **R8** With the Pilot monitor type, the first season (lowest number above 0) is not monitored. [3]
- **R9** Any other season is monitored only if it has monitored episodes after the episode update; otherwise it is not monitored. [3]

## Side effects

- **E1** Episode monitored flags are set for each series according to the chosen monitor type and the first and last season. [3]
- **E2** Each series, with its season monitoring changes, is saved through the series service. [3]

## Couldn't see

- the depth limit left out 4 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.SeasonPass.SeasonPassController.UpdateAll(Sonarr.Api.V3.SeasonPass.SeasonPassResource)`
2. `endpoint:Sonarr.Api.V3|POST /api/v3/SeasonPass`
3. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeMonitoredService.SetEpisodeMonitoredStatus(NzbDrone.Core.Tv.Series,NzbDrone.Core.Tv.MonitoringOptions)`

<sub>commit 0bffefe · inputs b2b19b5 · slice of 46 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

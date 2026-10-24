# Reconfigure logging on config save

`Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)` · backend · Sonarr.Core

From the code: ReconfigureLogging.HandleAsync(ConfigFileSavedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a config file saved event is received, the handler re-applies the logging configuration. [1][2]

## Rules

- **R1** The console minimum log level is the configured console log level if it is not blank. Otherwise it is the main log level if that is above Info, else Info. [2][3]
- **R2** The Info log file is enabled from Info level only when the log level is Info or lower, otherwise it is turned off. [2]
- **R3** The Debug log file is enabled from Debug level only when the log level is Debug or lower, otherwise it is turned off. [2]
- **R4** The Trace log file is enabled from Trace level only when the log level is Trace, otherwise it is turned off. [2]
- **R5** For each rule targeting a named target, levels below the minimum are disabled and levels from the minimum up (Trace, Debug, Info, Warn, Error, Fatal) are enabled. [4][5][6]
- **R6** A syslog target is set up only when a syslog server is configured and not blank. [2][3]
- **R7** The syslog target sends over UDP to the configured server and port, using RFC 5424 with the instance name as app name, a 500 retry base delay, and a rule for all loggers at the syslog level. [7]
- **R8** Sentry is enabled when running in production with analytics enabled, or when running in development. [8]
- **R9** The log size limit is configured in megabytes and converted to bytes for the archive size. [9][10]
- **R10** The console layout is the Clef format when the console log format is Clef, otherwise the cleansing console layout. [11][12]

## Side effects

- **E1** Log file targets get their maximum archive file count from the configured log rotate value and their archive size limit from the configured log size limit. [9]
- **E2** The console target's layout is changed according to the configured console log format. [12]
- **E3** The SQL logging flag is set from the configured LogSql value. [2]
- **E4** The Sentry target's enabled state and event filtering are updated from configuration. [8]
- **E5** A syslog target and a logging rule for it are added to the logging configuration. [7]
- **E6** All existing loggers are reconfigured after the changes. [2]

## Couldn't see

- ❓ `src/NzbDrone.Host/Bootstrap.cs:113` configuration section 'Sonarr:Log' is in no appsettings file of this project (environment variable or secret?)
- the depth limit left out 23 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.Reconfigure`
3. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`
4. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.SetMinimumLogLevel(System.Collections.Generic.IList{NLog.Config.LoggingRule},System.String,NLog.LogLevel)`
5. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.SetMinimumLogLevel(NLog.Config.LoggingRule,NLog.LogLevel)`
6. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.GetLogLevels`
7. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.SetSyslogParameters(System.String,System.Int32,NLog.LogLevel)`
8. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.ReconfigureSentry`
9. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.ReconfigureFile`
10. `Sonarr.Common|M:NzbDrone.Common.Extensions.NumberExtensions.Megabytes(System.Int32)`
11. `Sonarr.Common|M:NzbDrone.Common.Instrumentation.NzbDroneLogger.ConfigureConsoleLayout(NLog.Targets.ColoredConsoleTarget,NzbDrone.Common.Instrumentation.ConsoleLogFormat)`
12. `Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.ReconfigureConsole`

<sub>commit 0bffefe · inputs c36945c · slice of 75 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>

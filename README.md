# Red Mist Timing Frontend App
[![Build](https://github.com/bgriggs/redmist-timing-ui/actions/workflows/build.yml/badge.svg)](https://github.com/bgriggs/redmist-timing-scoring-backend/actions/workflows/build.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Red Mist provides race timing and scoring services for motorsport events. This repository contains the frontend application for the Red Mist Timing system, built with Avalonia. It is designed to be run on iOS, Android, Browsers, Windows, Linux, macOS, and other platforms.

# Building the Application
Reference the Avalonia documentation for editor setup:
https://docs.avaloniaui.net/docs/get-started/set-up-an-editor

You can build the application using the included solution file `RedMist.Timing.UI.sln` in Visual Studio or using the .NET CLI. To build from the command line, navigate to the project directory and run:
```bash
dotnet build RedMist.Timing.UI.sln
```

# Running the Application
For development, the most direct way to run the application is with the Windows Desktop variant of Avalonia. You can set it as the Startup Project, or run the exe from the debug folder.

## Client Key
To run the application, you will need a client key. This key is used to authenticate your instance of the application with the Red Mist Timing backend. Contact support@redmist.racing to request an API key. Alternatively, you can run the backend and a Keycloak authentication server.

# Development
The main project is `RedMist.Timing.UI`, which contains the Avalonia application code for the majority of the UI. The other projects are there for builds to specific environments, e.g. iOS. There are also shared libraries for data models.

## Crash Reporting
Crashes and errors are reported to Sentry. Reporting is **off** until a DSN is configured: with
`Sentry:Dsn` empty the SDK is never initialized and every reporting call is a no-op, so developer
builds stay silent.

To turn it on, create a Sentry project and provision the settings below. A DSN is a write-only
ingest key and is safe to embed in a client, so it can equally be committed directly to
`RedMist.Timing.UI/appsettings.json` instead. Use a single Sentry project for all heads: events are
tagged with `platform` (`android`/`ios`/`desktop`), so they can be filtered without splitting quota.

### CI settings

The Android and iOS publish workflows read these. Everything is optional - with none of them set the
workflows behave exactly as they did before, and the app runs with reporting off.

| GitHub setting | Kind | Purpose |
| --- | --- | --- |
| `SENTRY_DSN` | secret | Written into `secrets.release.json`, which layers over appsettings in Release. Not actually secret; stored as one for convenience. |
| `SENTRY_AUTH_TOKEN` | secret | **Genuinely secret.** Enables debug symbol upload. Without it stack traces from Release builds have no file names or line numbers. |
| `SENTRY_ORG` | variable | Sentry organization slug, for symbol upload. |
| `SENTRY_PROJECT` | variable | Sentry project slug, for symbol upload. |

Symbol upload turns itself on only when `SENTRY_AUTH_TOKEN` is present, so local and PR builds are
unaffected. Android uploads `elf` symbols for the native libraries - the part that turns a
`libmonosgen`-only tombstone into a readable frame - and iOS uploads dSYMs. Symbols only: source
upload is deliberately off. A bad token or an
unreachable Sentry downgrades to a build warning and skips the upload rather than failing a release.

The browser workflows are deliberately left alone: that head never initializes the SDK, so a DSN
there would be dead configuration.

Settings under the `Sentry` section:

| Key | Default | Purpose |
| --- | --- | --- |
| `Dsn` | *(empty)* | Sentry project DSN. Empty disables reporting entirely. |
| `Environment` | `production` | Environment tag on reported events. |
| `Debug` | `false` | Sentry SDK's own diagnostic logging. |
| `CrashOnUnhandledUiException` | `true` | Whether an unhandled UI-thread exception terminates the app. |

Errors reach Sentry through `ILogger`, so every existing catch block reports without changes at the
call site: `LogError` and above become events, and `LogWarning` and above become breadcrumbs.
Anything below `Warning` reaches only the on-device log, which is what makes the `Information`
downgrade for cancellation invisible to Sentry. The SDK
is started by each platform head (`CrashReporting.Init`) before Avalonia so startup faults are
covered. The browser head deliberately does not initialize it, since the transport is not supported
on WebAssembly.

### Reporting volume and resilience
Undelivered envelopes are cached under the platform's local application data
(`<LocalAppData>/RedMist/sentry`), so a crash that happens with no signal - the normal state of a
phone in a paddock - is written to disk and delivered on a later run rather than dying with the
process. Startup does not block on that delivery.

Connectivity failures are throttled to 3 per 10-minute window. `HubClient` reconnects on an infinite
retry policy and logs an error per attempt, so an afternoon on bad cell service would otherwise spend
a month's allowance on the free tier and bury the faults worth reading. The window resets, so a
genuine backend outage hours into a race day is still visible. Ordinary faults are never throttled.

Cancellation of background work is logged at Information rather than Error, so it stays in the
on-device log without becoming a Sentry event. Note that an `HttpClient` timeout surfaces as a
`TaskCanceledException` but is *not* treated as cancellation - it is a real failure, and the app's
most common one.

### Unhandled UI-thread exceptions
`CrashOnUnhandledUiException` defaults to `true`, which means these crash the app rather than being
suppressed - but only when a DSN is configured. With reporting off, terminating would cost the user
their session and produce no evidence in exchange, so a build without a DSN suppresses exactly as it
always did. This is deliberate. Marking them handled did not avoid the crash, it only removed the
evidence: the app carried on with the state the aborted operation left behind and died later
somewhere unrelated, leaving a tombstone with nothing but `libmonosgen` frames. Set it to `false` to
go back to suppressing - the exception is still reported either way.

## Event Only Mode
When hosting the application in the web, the event selection is delegated to the website. Therefore, the application directly routes to the event in the mode. It uses the page URL with event parameter to accomplish this. For development, you can pass the event ID as a command line argument to the application. For example, set the args in the Desktop launchSettings.json:
```bash
"commandLineArgs": "29"
```
Where 29 is the event ID.



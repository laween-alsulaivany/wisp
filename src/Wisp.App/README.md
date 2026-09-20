# App shell (Phase 10)

Run `dotnet build Wisp.sln`, then launch
`src/Wisp.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Wisp.App.exe`.
Startup is windowless. Windows may place the W icon in its tray overflow.
Right-click it for Pick for me, Library, History, Settings, and Exit.
The first four commands open empty windows; closing one returns to the tray.
Ctrl+Shift+P opens the same Pick for me placeholder when the hotkey is available.
There is no recommendation UI, feedback popup, or attached Steam button yet.

This development build requires .NET 10 and the pinned Windows App SDK runtime.
Self-contained distribution packaging remains a later phase.

`AppHost` validates the production registrations and initializes the database
before any worker starts. The user database is `%LOCALAPPDATA%/Wisp/wisp.db`.
Metadata scans at startup and runs delta sync every five minutes. The session
host discovers the active local Steam profile, then owns the Phase 7 tracker for
that profile. It checks profile changes every 30 seconds and stops the previous
tracker before switching; no Steam profile means no invented session owner.
All window consumers share one foreground monitor. Exit stops the host and
disposes the icon, hotkey, native monitor, clients, and logging resources.

The update worker checks the public GitHub latest-release endpoint once at
startup and every 24 hours. Its dedicated client has no authentication, cookies,
default credentials, or automatic redirects. The request contains no query or
body, and only a constant `User-Agent: Wisp` and JSON Accept header. Newer stable
versions add an Update available item before Exit, opening the GitHub release
page only when clicked. Missing releases and network failures never show a
dialog or trigger installation. Failure details stay in the local log.

Logging has one Serilog file sink at `%LOCALAPPDATA%/Wisp/logs/wisp-*.log`, rotated
daily and at 10 MB, retaining seven files. The host clears default logging
providers, loads no sink configuration, and routes earlier layers' Trace output
to this same sink. No telemetry or remote logging sink is configured. Artwork
and update HTTP clients perform product functions and do not transport logs.

`assets/completion-estimates.db` is a schema-valid, empty placeholder. Game
Duration is deliberately inert until a real dataset is supplied in a later
release. Startup refreshes the database tag dictionary from the single shared
`assets/tags.json`; it is not a one-time schema migration.

The test project compiles the same non-UI service sources as the app, following
the existing Phase 9 test arrangement. It validates all phase-relevant Core
interfaces and all hosted registrations without loading WinUI. HTTP tests use
fake handlers only. `IStartupRegistrar` belongs to Phase 12 and is not registered
yet. Unit tests do not replace the live tray and hosted-service startup gate.

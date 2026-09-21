# Wisp app (Phase 13)

Run `dotnet build Wisp.sln`, then launch
`src/Wisp.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Wisp.App.exe`.
Startup shows the minimal welcome screen when no SteamProfiles row exists.
After a profile has been recorded, subsequent launches go straight to the tray.
Windows may place the W icon in its tray overflow.
Right-click it for Pick for me, Library, History, Settings, and Exit.
Pick for me opens one recommendation with clickable artwork, collapsed reasons,
Mood/Time choices, and Reroll. Closing it starts a fresh picking session next
time, resetting filters and reroll exclusions. Ctrl+Shift+P and the Steam-attached
button open the same picker. Library, History, and Settings expose the Phase 12
profile-scoped screens.

More filters expands to Coming soon; every request uses `AdvancedFilters.None`.
Time uses the PRD's generic 30/75/150/240-minute choices. Personalized time choices
need an engine/service contract in later work; no distribution calculation lives
in these view models. Artwork failure leaves a playable placeholder. Play calls
`ISessionTracker.NotifyRecommendationLaunch(appId)` immediately before the Steam
URI launch. Snapshots go directly from the existing provider to the engine.

The attached button follows Steam's physical bounds and DPI, expands on hover,
and hides on focus loss or in Big Picture. It does not activate when clicked.
Its small orange badge indicates pending feedback for the active profile.
Tracking/positioning failures leave the tray and hotkey available.

Finalized sessions under five minutes stay silent. Five through fifteen minutes
show Keep going / Not feeling it / Technical issue / Interrupted; longer sessions
show Keep going / Maybe later / Finished / Drop. Not now or closing the popup
records Pending through the existing state service. Each session is presented
at most once per app run, and startup never reopens pending feedback. The existing
ShowPostSessionFeedback setting is read without modification. Exit saves pending
answers before stopping the host. Artwork Play fades upward on hover or keyboard
focus; the attached button expands leftward on hover while keeping its right
edge anchored. Both honor Windows' animation-effects setting.

Build development versions with the .NET 10 SDK. For distribution, run
`pwsh -File src/Wisp.App/Publish-Distribution.ps1` from the repository root.
It produces an unsigned, self-contained, single-file Windows x64 executable and
`FIRST_RUN.md` in a ZIP. See [FIRST_RUN.md](FIRST_RUN.md) for launch instructions
and [PHASE_13_CHECKLIST.md](PHASE_13_CHECKLIST.md) for the pending manual release
checks. A successful publish alone does not establish clean-machine compatibility.

The welcome gate is captured after migrations and before any profile-writing
worker starts. Its game count waits for the existing startup sync and initial
profile discovery. Get started only dismisses the welcome; it never creates a
profile or writes a separate onboarding flag. Closing the welcome exits Wisp.
Once a profile exists, even closing before Get started skips the welcome on the
next launch, as required by the profile-row gate. Missing Steam or an active
account leaves Get started disabled with guidance to open/sign in to Steam and
restart Wisp. Tray actions and the hotkey bring the welcome forward until it is
dismissed, and the attached button remains hidden during the welcome.

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

The test project compiles the same non-UI service and view-model sources as the app, following
the existing Phase 9 test arrangement. It validates all phase-relevant Core
interfaces and all hosted registrations without loading WinUI. HTTP tests use
fake handlers only. `IStartupRegistrar` is registered for Phase 12 Settings.
Phase 11 tests cover request/exclusion isolation, unchanged snapshots,
always-None advanced filters, launch attribution order, duration variants and
feedback delegation, stale responses, artwork failure, and button event/DPI
behavior. Phase 13 tests cover first-launch ordering and persistence with a real
temporary database, count/readiness, failure, cancellation, and one-shot dismissal.
No live window or UI-automation harness is used. The manual checklist records
the remaining visual, multi-monitor, and clean-machine verification gates.

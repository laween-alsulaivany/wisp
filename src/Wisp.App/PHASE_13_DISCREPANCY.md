# Phase 13 blocked: live Steam window ownership

Live desktop discovery on 2026-09-20 reported the window titled `Steam` as owned
by `Steam/bin/cef/cef.win64/steamwebhelper.exe`, not `steam.exe`.

The Phase 6 implementation in
`src/Wisp.SteamIntegration/Win32/WindowTrackingNative.cs`,
`EnumerateSteamWindows`, only collects PIDs from
`Process.GetProcessesByName("steam")` and rejects windows outside that set.
`WindowGeometry.PickMainWindow` also only accepts `steam` or `steam.exe`.
Consequently the actual client window observed here cannot be selected, so the
Phase 13 Steam-attached animation and DPI-follow checks cannot be completed
against this client through the existing service.

This follows the original section 4.1 process-owner assumption, but the live
client does not satisfy it. It is a Phase 6 discovery implementation/specification
gap, not missing animation code. Changing discovery in a view or adding a second
window tracker in Wisp.App would violate the Phase 13 boundary.

Required prerequisite: separately authorize a narrowly scoped SteamIntegration
change that identifies the current Steam client's top-level window, including
its verified helper-process ownership, while preserving the shared foreground
hook, largest-window selection, Big Picture behavior, and fail-silent fallback.
Add deterministic native-seam tests for the observed ownership case and verify
against live Steam before resuming Phase 13's attached-button checks. The
read-only technical plan has not been edited.

Phase 13 implementation work stopped at this finding. Existing app changes and
the unsigned ZIP are preserved for review; the draft PR is not release-ready.
The full solution build and 413 tests passed before this finding. Clean-machine
execution, cold-start UI, animations, and mixed-DPI manual checks remain pending.

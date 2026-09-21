# Phase 13 verification

Automated checks cover startup ordering, a fresh real SQLite database, the
profile-row gate on the next startup, and the first-launch view model. They do
not establish that the WinUI screens render correctly or that the executable
runs on a clean Windows machine.

Run `dotnet build Wisp.sln` and `dotnet test Wisp.sln` from the repository root.
Run `pwsh -File src/Wisp.App/Publish-Distribution.ps1` to publish and ZIP an
unsigned, self-contained x64 executable with `FIRST_RUN.md`. Each run writes a
new timestamped folder under `src/Wisp.App/bin/distribution`, including the
publish output, log, archive, and SHA-256 checksum. No existing output is deleted.

## Manual release gate

All boxes below are pending live verification. Record Windows version, artifact
SHA-256, monitor scaling, and results before marking the release ready.

- [ ] On a clean Windows x64 VM with no .NET SDK/runtime or Windows App SDK
  runtime installed, extract the ZIP outside the source checkout. Verify it
  contains `Wisp.App.exe` and `FIRST_RUN.md`. Launch the EXE directly and confirm
  no missing-runtime, native-DLL, resource, icon, or SQLite asset errors.
- [ ] With Steam installed and a local account signed in, use a fresh Windows
  test account with no `%LOCALAPPDATA%\Wisp` data. Do not delete real user data.
  Verify the welcome screen says Steam found / N installed games / Get started,
  with N matching the installed Steam manifests. Get started must remain disabled
  while startup loads. Tray and hotkey actions should bring this screen forward.
- [ ] Select Get started. Confirm the welcome closes and tray actions work.
  Exit using the tray, relaunch twice, and confirm no welcome screen reappears.
  Repeat with another local Steam account: any existing Wisp SteamProfiles row
  suppresses first launch, regardless of the currently active account.
- [ ] Verify an empty Steam library shows 0 installed games and allows continuing.
  Without Steam or a detectable account, verify restart guidance and a disabled
  Get started button. Closing the welcome exits. A stored profile is the sole
  persistent gate: closing after profile discovery also skips welcome next time.
- [ ] Hover the recommendation artwork: Play fades upward from the image's bottom
  without spilling beyond it. Rapidly enter/leave and verify smooth reversal.
  Tab to the artwork and verify Play is visible with keyboard focus. Click the
  image or use Enter and confirm the currently displayed game launches.
- [ ] Focus Steam and hover the W button: it expands leftward to Pick for me over
  about 180 ms, keeping its right edge anchored. Rapid hover changes must not
  leave it stuck. Click it and verify the picker opens. Alt-tab away and verify
  the button hides; return and verify it appears collapsed. Check Big Picture
  suppression and the pending-feedback badge.
- [ ] On real monitors with different scales (for example 100% and 150%), drag
  and resize Steam across both monitors, including a monitor left of the primary
  with negative coordinates. Test at 200% if available. Verify the button follows
  the bottom-right corner, remains clickable, and scales correctly while expanded
  and collapsed. If tracking becomes unavailable, tray and Ctrl+Shift+P must
  remain usable. Record unavailable hardware explicitly instead of passing it.
- [ ] With Windows animation effects disabled, verify the Play overlay and Steam
  button switch directly to their target state. Re-enable effects and retest.
- [ ] For a downloaded unsigned ZIP, follow FIRST_RUN.md: Windows protected your
  PC, More info, Run anyway (when offered by Windows policy). Verify no app code
  disables, changes, or suppresses SmartScreen. A locally built file without a
  download mark may not show the prompt.
- [ ] Exit and inspect `%LOCALAPPDATA%\Wisp\logs` for startup/resource errors.

Packaging settings follow Microsoft's [unpackaged WinUI distribution guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app).
`EnableMsixTooling` supplies resource generation for the single-file EXE;
`WindowsPackageType=None` keeps this distribution unpackaged.

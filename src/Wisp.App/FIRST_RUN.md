# Running Wisp for the first time

Extract the ZIP into a folder you want to keep, then open `Wisp.App.exe`.
This Windows x64 build includes .NET 10 and the Windows App SDK runtime.
You do not need to install the .NET SDK or runtime separately.

Wisp v1 is deliberately unsigned. On a downloaded copy, Microsoft Defender
SmartScreen may show **Windows protected your PC**. If you trust the source of
your download, select **More info**, then **Run anyway** to launch Wisp.
Windows may display an unknown publisher because the executable is unsigned.
If your organization's policy does not offer Run anyway, contact its administrator.
Do not disable SmartScreen or other Windows protections.

Have Steam installed and signed in locally. Wisp does not ask for your Steam
password or link your account. On a fresh setup, Wisp shows **Steam found**,
the number of installed games, and **Get started**. Select Get started to
continue to the system tray. Later launches go straight to the tray once a
Steam profile has been recorded. Windows may put the W icon in the tray overflow.
Right-click it for Pick for me, Library, History, Settings, and Exit.
Ctrl+Shift+P also opens the picker.

Keep the executable in its chosen folder if you enable Start with Windows.
The single-file executable extracts its bundled runtime and resources on first
use; that first launch can take longer. Your Wisp data and logs stay in
`%LOCALAPPDATA%\Wisp`. The bundled completion-time dataset is currently empty,
so game-duration estimates are not available in this release.

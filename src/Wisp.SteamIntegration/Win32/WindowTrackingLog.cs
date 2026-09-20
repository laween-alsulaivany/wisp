using System.Diagnostics;

namespace Wisp.SteamIntegration.Win32;

internal static class WindowTrackingLog
{
    public static void Failure(Exception exception)
    {
        try
        {
            Trace.TraceWarning($"Win32 integration failure: {exception}");
        }
        catch (Exception)
        {
            // A failing local trace listener must not defeat the fail-silent boundary.
        }
    }
}

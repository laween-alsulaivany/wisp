namespace Wisp.SteamIntegration.Win32;

internal readonly record struct WindowRect(int Left, int Top, int Right, int Bottom)
{
    public long Area => Math.Max(0L, (long)Right - Left) * Math.Max(0L, (long)Bottom - Top);
}

internal readonly record struct WindowCandidate(nint Handle, int ProcessId, string ProcessName,
    string Title, WindowRect ClientBounds);

public readonly record struct OverlayAnchor(double X, double Y);

public static class WindowGeometry
{
    /// <summary>Returns a physical screen point inset from the visual bottom-right by logical pixels.</summary>
    public static OverlayAnchor CalculateOverlayAnchor(int right, int bottom, uint dpi,
        double horizontalOffset, double verticalOffset) =>
        new(right - horizontalOffset * dpi / 96.0, bottom - verticalOffset * dpi / 96.0);

    internal static bool IsBigPicture(string processName, WindowRect bounds, WindowRect monitorBounds) =>
        IsSteam(processName) && bounds == monitorBounds;

    internal static WindowCandidate? PickMainWindow(IEnumerable<WindowCandidate> windows) =>
        windows.Where(window => IsSteam(window.ProcessName) && !string.IsNullOrWhiteSpace(window.Title))
            .OrderByDescending(window => window.ClientBounds.Area)
            .Select(window => (WindowCandidate?)window)
            .FirstOrDefault();

    private static bool IsSteam(string processName) =>
        string.Equals(processName, "steam", StringComparison.OrdinalIgnoreCase)
        || string.Equals(processName, "steam.exe", StringComparison.OrdinalIgnoreCase);
}

namespace Wisp.SteamIntegration.Win32;

internal interface IWindowTrackingNative : IDisposable
{
    void Start(Action<nint> foregroundChanged, Action<nint> windowChanged, Action<Exception> failed);
    int? GetProcessId(nint window);
    IReadOnlyList<WindowCandidate> EnumerateSteamWindows();
    WindowRect GetFrameBounds(nint window);
    WindowRect GetMonitorBounds(nint window);
    uint GetDpi(nint window);
}

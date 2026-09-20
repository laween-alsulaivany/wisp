using Wisp.SteamIntegration.Win32;

namespace Wisp.SteamIntegration.Tests;

internal sealed class FakeWindowTrackingNative : IWindowTrackingNative
{
    public List<WindowCandidate> Windows { get; } = [new(10, 42, "steam", "Steam", new(0, 0, 800, 600))];
    public WindowRect Frame { get; set; } = new(0, 0, 800, 600);
    public WindowRect Monitor { get; set; } = new(0, 0, 1920, 1080);
    public uint Dpi { get; set; } = 96;
    public int? ForegroundPid { get; set; }
    public string? ThrowOn { get; set; }
    public int Starts { get; private set; }
    public int Disposals { get; private set; }
    public int DpiReads { get; private set; }
    public int Enumerations { get; private set; }
    private Action<nint>? foreground;
    private Action<nint>? changed;
    private Action<Exception>? failed;

    public void Start(Action<nint> foregroundChanged, Action<nint> windowChanged, Action<Exception> failed)
    {
        Starts++;
        foreground = foregroundChanged;
        changed = windowChanged;
        this.failed = failed;
        Check("Start");
        foreground(0);
        changed(0);
    }

    public void Foreground(int? processId)
    {
        ForegroundPid = processId;
        foreground!(0);
    }

    public void WindowChanged() => changed!(10);
    public void Fail() => failed!(new InvalidOperationException("Native hook failed."));

    public int? GetProcessId(nint window)
    {
        Check("ProcessId");
        return ForegroundPid;
    }

    public IReadOnlyList<WindowCandidate> EnumerateSteamWindows()
    {
        Enumerations++;
        Check("Enumerate");
        return Windows;
    }

    public WindowRect GetFrameBounds(nint window)
    {
        Check("Bounds");
        return Frame;
    }

    public WindowRect GetMonitorBounds(nint window)
    {
        Check("Monitor");
        return Monitor;
    }

    public uint GetDpi(nint window)
    {
        DpiReads++;
        Check("Dpi");
        return Dpi;
    }

    public void Dispose()
    {
        Disposals++;
        Check("Dispose");
    }

    private void Check(string operation)
    {
        if (ThrowOn == operation)
            throw new InvalidOperationException($"Failed {operation}.");
    }
}

using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Win32;

/// <summary>Shares the monitor's hook without owning or stopping its generic foreground signal.</summary>
public sealed class SteamWindowTracker : ISteamWindowTracker, IDisposable
{
    private readonly ForegroundWindowMonitor monitor;
    private readonly IWindowTrackingNative native;
    private readonly object gate = new();
    private WindowCandidate? mainWindow;
    private SteamWindowBoundsChangedEventArgs? bounds;
    private bool focused;
    private bool disabled;
    private bool bigPicture;

    public SteamWindowTracker(ForegroundWindowMonitor monitor)
    {
        this.monitor = monitor;
        native = monitor.Native;
        monitor.WindowChanged += OnWindowChanged;
        monitor.ForegroundProcessChanged += OnForegroundChanged;
        monitor.Failed += Disable;
    }

    public event EventHandler<SteamWindowBoundsChangedEventArgs>? BoundsChanged;
    public event EventHandler? SteamFocusLost;
    public event EventHandler? SteamFocusGained;

    public bool IsBigPictureMode
    {
        get { lock (gate) return bigPicture; }
    }

    private void OnForegroundChanged(object? sender, ForegroundProcessChangedEventArgs args) => Refresh();

    private void OnWindowChanged(nint window) => Refresh();

    private void Refresh()
    {
        lock (gate)
        {
            if (disabled)
                return;
            try
            {
                var next = WindowGeometry.PickMainWindow(native.EnumerateSteamWindows());
                if (mainWindow?.Handle != next?.Handle)
                    bounds = null;
                mainWindow = next;
                var nextFocused = next.HasValue && next.Value.ProcessId == monitor.CurrentForegroundProcessId;
                if (focused != nextFocused)
                {
                    focused = nextFocused;
                    if (focused) SteamFocusGained?.Invoke(this, EventArgs.Empty);
                    else SteamFocusLost?.Invoke(this, EventArgs.Empty);
                }
                if (disabled)
                    return;
                if (next is not { } window)
                {
                    bigPicture = false;
                    bounds = null;
                    return;
                }

                var frame = native.GetFrameBounds(window.Handle);
                var dpi = native.GetDpi(window.Handle);
                bigPicture = WindowGeometry.IsBigPicture(window.ProcessName, frame, native.GetMonitorBounds(window.Handle));
                var current = new SteamWindowBoundsChangedEventArgs(frame.Left, frame.Top, frame.Right, frame.Bottom, dpi);
                if (current != bounds)
                {
                    bounds = current;
                    BoundsChanged?.Invoke(this, current);
                }
            }
            catch (Exception exception)
            {
                Disable(exception);
            }
        }
    }

    private void Disable(Exception exception)
    {
        lock (gate)
        {
            disabled = true;
            bigPicture = false;
            WindowTrackingLog.Failure(exception);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disabled = true;
            bigPicture = false;
        }
        monitor.WindowChanged -= OnWindowChanged;
        monitor.ForegroundProcessChanged -= OnForegroundChanged;
        monitor.Failed -= Disable;
    }
}

using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Win32;

/// <summary>
/// Own one instance per application. Attach the Steam tracker and subscribers before Start.
/// Events run on the native message thread; UI consumers must dispatch to their UI thread.
/// </summary>
public sealed class ForegroundWindowMonitor : IForegroundWindowMonitor, IDisposable
{
    private readonly object gate = new();
    private readonly IWindowTrackingNative native;
    private bool started;
    private bool disabled;
    private bool disposed;
    private int? processId;

    public ForegroundWindowMonitor() : this(new WindowTrackingNative()) { }

    internal ForegroundWindowMonitor(IWindowTrackingNative native) => this.native = native;

    internal IWindowTrackingNative Native => native;
    internal event Action<nint>? WindowChanged;
    internal event Action<Exception>? Failed;

    public event EventHandler<ForegroundProcessChangedEventArgs>? ForegroundProcessChanged;

    public int? CurrentForegroundProcessId
    {
        get { lock (gate) return processId; }
    }

    public void Start()
    {
        lock (gate)
        {
            if (started || disabled || disposed)
                return;
            started = true;
        }

        try
        {
            native.Start(OnForegroundChanged, OnWindowChanged, Disable);
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    private void OnForegroundChanged(nint window)
    {
        lock (gate)
        {
            if (disabled || disposed)
                return;
            try
            {
                var next = native.GetProcessId(window);
                if (processId == next)
                    return;
                processId = next;
                ForegroundProcessChanged?.Invoke(this, new(next));
            }
            catch (Exception exception)
            {
                Disable(exception);
            }
        }
    }

    private void OnWindowChanged(nint window)
    {
        lock (gate)
        {
            if (disabled || disposed)
                return;
            try { WindowChanged?.Invoke(window); }
            catch (Exception exception) { Disable(exception); }
        }
    }

    private void Disable(Exception exception)
    {
        lock (gate)
        {
            if (disabled || disposed)
                return;
            disabled = true;
            processId = null;
            WindowTrackingLog.Failure(exception);
            try { Failed?.Invoke(exception); }
            catch (Exception failure) { WindowTrackingLog.Failure(failure); }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            processId = null;
        }
        try { native.Dispose(); }
        catch (Exception exception) { WindowTrackingLog.Failure(exception); }
    }
}

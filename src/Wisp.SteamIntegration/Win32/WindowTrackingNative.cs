using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace Wisp.SteamIntegration.Win32;

internal sealed class WindowTrackingNative : IWindowTrackingNative
{
    private readonly ManualResetEventSlim ready = new();
    private readonly object lifetimeGate = new();
    private bool disposed;
    private Thread? thread;
    private uint threadId;
    private WinEventProc? foregroundCallback;
    private WinEventProc? windowCallback;
    private const uint RefreshMessage = (uint)WindowMessage.WM_APP + 1;

    public void Start(Action<nint> foregroundChanged, Action<nint> windowChanged, Action<Exception> failed)
    {
        lock (lifetimeGate)
        {
            if (disposed || thread is not null)
                return;
            thread = new Thread(() => Run(foregroundChanged, windowChanged, failed))
            {
                IsBackground = true,
                Name = "Wisp window events"
            };
            thread.Start();
            ready.Wait();
        }
    }

    private void Run(Action<nint> foregroundChanged, Action<nint> windowChanged, Action<Exception> failed)
    {
        SafeHWINEVENTHOOK? foregroundHook = null;
        SafeHWINEVENTHOOK? windowHook = null;
        var refreshPending = false;
        try
        {
            threadId = Kernel32.GetCurrentThreadId();
            PeekMessage(out _, default, 0, 0, PM.M_NOREMOVE);
            foregroundCallback = (_, _, window, _, _, _, _) =>
            {
                try { foregroundChanged((nint)window); }
                catch (Exception exception) { failed(exception); }
            };
            windowCallback = (_, _, window, objectId, childId, _, _) =>
            {
                try
                {
                    if (objectId != 0 || childId != 0 || refreshPending)
                        return;
                    // Coalesce bursts from moves/resizes before enumerating the top-level windows.
                    refreshPending = true;
                    if (!PostThreadMessage(threadId, RefreshMessage, 0, 0))
                        throw new Win32Exception();
                }
                catch (Exception exception) { failed(exception); }
            };
            foregroundHook = SetWinEventHook(EventConstant.EVENT_SYSTEM_FOREGROUND,
                EventConstant.EVENT_SYSTEM_FOREGROUND, default, foregroundCallback, 0, 0, WINEVENT.WINEVENT_OUTOFCONTEXT);
            if (foregroundHook.IsInvalid)
                throw new Win32Exception();
            windowHook = SetWinEventHook(EventConstant.EVENT_OBJECT_CREATE, EventConstant.EVENT_OBJECT_NAMECHANGE,
                default, windowCallback, 0, 0, WINEVENT.WINEVENT_OUTOFCONTEXT);
            if (windowHook.IsInvalid)
                throw new Win32Exception();

            ready.Set();
            foregroundChanged((nint)GetForegroundWindow());
            windowChanged(0);
            while (true)
            {
                var result = (int)GetMessage(out var message, default, 0, 0);
                if (result == -1)
                    throw new Win32Exception();
                if (result == 0)
                    break;
                if (message.message == RefreshMessage)
                {
                    refreshPending = false;
                    windowChanged(0);
                }
                else
                {
                    TranslateMessage(message);
                    DispatchMessage(message);
                }
            }
        }
        catch (Exception exception) { failed(exception); }
        finally
        {
            // Hooks must be released on the same thread that registered them.
            try { windowHook?.Dispose(); }
            catch (Exception exception) { failed(exception); }
            try { foregroundHook?.Dispose(); }
            catch (Exception exception) { failed(exception); }
            ready.Set();
            GC.KeepAlive(foregroundCallback);
            GC.KeepAlive(windowCallback);
        }
    }

    public int? GetProcessId(nint window)
    {
        if (window == 0)
            return null;
        GetWindowThreadProcessId(window, out var processId);
        return processId == 0 ? null : checked((int)processId);
    }

    public IReadOnlyList<WindowCandidate> EnumerateSteamWindows()
    {
        var processIds = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("steam"))
        {
            using (process)
                processIds.Add(process.Id);
        }

        var windows = new List<WindowCandidate>();
        Exception? failure = null;
        var result = EnumWindows((window, _) =>
        {
            try
            {
                var processId = GetProcessId((nint)window);
                if (processId is not { } pid || !processIds.Contains(pid) || !IsWindowVisible(window))
                    return true;
                GetWindowText(window, out var title);
                if (string.IsNullOrWhiteSpace(title))
                    return true;
                if (!GetClientRect(window, out var client))
                    throw new Win32Exception();
                windows.Add(new((nint)window, pid, "steam", title, ToRect(client)));
                return true;
            }
            catch (Exception exception)
            {
                // Never unwind a managed exception through the native EnumWindows callback.
                failure = exception;
                return false;
            }
        }, 0);
        if (failure is not null)
            throw failure;
        if (!result)
            throw new Win32Exception();
        return windows;
    }

    public WindowRect GetFrameBounds(nint window)
    {
        DwmApi.DwmGetWindowAttribute(window, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            out RECT bounds).ThrowIfFailed();
        return ToRect(bounds);
    }

    public WindowRect GetMonitorBounds(nint window)
    {
        var monitor = MonitorFromWindow(window, MonitorFlags.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            throw new Win32Exception();
        return ToRect(info.rcMonitor);
    }

    public uint GetDpi(nint window)
    {
        var dpi = GetDpiForWindow(window);
        return dpi == 0 ? throw new Win32Exception("Cannot read window DPI.") : dpi;
    }

    private static WindowRect ToRect(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);

    public void Dispose()
    {
        Thread? owner;
        lock (lifetimeGate)
        {
            if (disposed)
                return;
            disposed = true;
            owner = thread;
            if (owner is { IsAlive: true } && !PostThreadMessage(threadId, (uint)WindowMessage.WM_QUIT, 0, 0))
                throw new Win32Exception();
        }
        if (owner is not null && Thread.CurrentThread != owner)
            owner.Join();
    }
}

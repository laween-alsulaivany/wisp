using Wisp.Core.Enums;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Win32;

public sealed class HotkeyManager : IHotkeyManager, IDisposable
{
    private readonly IHotkeyNative native;
    private volatile bool disposed;

    public HotkeyManager() : this(new HotkeyNative()) { }

    internal HotkeyManager(IHotkeyNative native) => this.native = native;

    public event EventHandler? PickHotkeyPressed;

    public bool RegisterPickHotkey(HotkeyModifiers modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
        uint virtualKey = 0x50)
    {
        if (disposed)
            return false;
        try { return native.Register(modifiers, virtualKey, OnPressed); }
        catch (Exception exception)
        {
            WindowTrackingLog.Failure(exception);
            return false;
        }
    }

    private void OnPressed()
    {
        if (disposed)
            return;
        try { PickHotkeyPressed?.Invoke(this, EventArgs.Empty); }
        catch (Exception exception) { WindowTrackingLog.Failure(exception); }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try { native.Dispose(); }
        catch (Exception exception) { WindowTrackingLog.Failure(exception); }
    }
}

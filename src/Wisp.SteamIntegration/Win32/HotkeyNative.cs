using System.ComponentModel;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using Wisp.Core.Enums;
using static Vanara.PInvoke.User32;

namespace Wisp.SteamIntegration.Win32;

internal sealed class HotkeyNative : IHotkeyNative
{
    private const uint RegisterMessage = (uint)WindowMessage.WM_APP + 1;
    private readonly object gate = new();
    private readonly ManualResetEventSlim ready = new();
    private Thread? thread;
    private HWND window;
    private WindowProc? windowProcedure;
    private Action? pressed;
    private int hotkeyId;
    private (HotKeyModifiers Modifiers, uint Key)? binding;
    private bool disposed;

    public bool Register(HotkeyModifiers modifiers, uint virtualKey, Action pressed)
    {
        HWND target;
        lock (gate)
        {
            if (disposed || (modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control
                | HotkeyModifiers.Shift | HotkeyModifiers.Windows)) != 0 || virtualKey is 0 or > 0xFE)
                return false;
            this.pressed = pressed;
            if (thread is null)
            {
                thread = new Thread(Run) { IsBackground = true, Name = "Wisp global hotkey" };
                thread.Start();
                ready.Wait();
            }
            if (window.IsNull)
                return false;
            target = window;
        }
        // Do not hold the lifecycle lock while the message thread calls subscribers.
        return SendMessage(target, RegisterMessage, (nint)(uint)modifiers, (nint)virtualKey) != 0;
    }

    private void Run()
    {
        var className = $"Wisp.Hotkey.{Guid.NewGuid():N}";
        HINSTANCE instance = default;
        var registered = false;
        try
        {
            instance = Kernel32.GetModuleHandle(null);
            windowProcedure = WindowProcedure;
            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                hInstance = instance,
                lpszClassName = className,
                lpfnWndProc = windowProcedure
            };
            if (RegisterClassEx(in windowClass).IsInvalid)
                throw new Win32Exception();
            registered = true;
            window = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, HWND.HWND_MESSAGE,
                default, instance, 0);
            if (window.IsNull)
                throw new Win32Exception();
            ready.Set();
            while (true)
            {
                var result = (int)GetMessage(out var message, default, 0, 0);
                if (result == -1)
                    throw new Win32Exception();
                if (result == 0)
                    break;
                TranslateMessage(message);
                DispatchMessage(message);
            }
        }
        catch (Exception exception) { WindowTrackingLog.Failure(exception); }
        finally
        {
            try
            {
                if (!window.IsNull)
                {
                    if (hotkeyId != 0) UnregisterHotKey(window, hotkeyId);
                    DestroyWindow(window);
                    window = default;
                }
                if (registered) UnregisterClass(className, instance);
            }
            catch (Exception exception) { WindowTrackingLog.Failure(exception); }
            ready.Set();
            GC.KeepAlive(windowProcedure);
        }
    }

    private nint WindowProcedure(HWND handle, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (message == RegisterMessage)
            {
                var nextId = hotkeyId == 1 ? 2 : 1;
                var modifiers = (HotKeyModifiers)(uint)wParam | HotKeyModifiers.MOD_NOREPEAT;
                if (binding == (modifiers, (uint)lParam))
                    return 1;
                if (!RegisterHotKey(handle, nextId, modifiers, (uint)lParam))
                    return 0;
                if (hotkeyId != 0 && !UnregisterHotKey(handle, hotkeyId))
                {
                    UnregisterHotKey(handle, nextId);
                    return 0;
                }
                hotkeyId = nextId;
                binding = (modifiers, (uint)lParam);
                return 1;
            }
            if (message == (uint)WindowMessage.WM_HOTKEY && (int)wParam == hotkeyId)
            {
                pressed?.Invoke();
                return 0;
            }
            if (message == (uint)WindowMessage.WM_CLOSE)
            {
                PostQuitMessage(0);
                return 0;
            }
            return DefWindowProc(handle, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            WindowTrackingLog.Failure(exception);
            return 0;
        }
    }

    public void Dispose()
    {
        Thread? owner;
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            owner = thread;
            if (!window.IsNull && !PostMessage(window, (uint)WindowMessage.WM_CLOSE, 0, 0))
                throw new Win32Exception();
        }
        if (owner is not null && owner != Thread.CurrentThread)
            owner.Join();
    }
}

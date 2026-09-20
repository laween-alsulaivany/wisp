using Wisp.Core.Enums;
using Wisp.SteamIntegration.Win32;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class HotkeyManagerTests
{
    [Fact]
    public void DefaultBindingIsControlShiftPAndHotkeySurvivesTrackerFailure()
    {
        var windows = new FakeWindowTrackingNative { ThrowOn = "Start" };
        using var monitor = new ForegroundWindowMonitor(windows);
        using var tracker = new SteamWindowTracker(monitor);
        var native = new FakeHotkeyNative();
        using var hotkey = new HotkeyManager(native);
        var presses = 0;
        hotkey.PickHotkeyPressed += (_, _) => presses++;
        monitor.Start();
        Assert.True(hotkey.RegisterPickHotkey());
        Assert.Equal((HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x50u), native.Binding);
        native.Press();
        Assert.Equal(1, presses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationFailureReturnsFalse(bool throws)
    {
        using var hotkey = new HotkeyManager(new FakeHotkeyNative { Success = false, Throws = throws });
        Assert.False(hotkey.RegisterPickHotkey(HotkeyModifiers.Alt, 0x51));
    }

    [Fact]
    public void SubscriberExceptionDoesNotEscapeAndDisposalStopsEvents()
    {
        var native = new FakeHotkeyNative();
        var hotkey = new HotkeyManager(native);
        var presses = 0;
        hotkey.PickHotkeyPressed += (_, _) =>
        {
            presses++;
            throw new InvalidOperationException();
        };
        hotkey.RegisterPickHotkey();
        Assert.Null(Record.Exception(native.Press));
        hotkey.Dispose();
        hotkey.Dispose();
        native.Press();
        Assert.Equal(1, presses);
        Assert.Equal(1, native.Disposals);
        Assert.False(hotkey.RegisterPickHotkey());
    }

    private sealed class FakeHotkeyNative : IHotkeyNative
    {
        public bool Success { get; init; } = true;
        public bool Throws { get; init; }
        public (HotkeyModifiers, uint) Binding { get; private set; }
        public int Disposals { get; private set; }
        private Action? pressed;

        public bool Register(HotkeyModifiers modifiers, uint virtualKey, Action pressed)
        {
            if (Throws) throw new InvalidOperationException();
            Binding = (modifiers, virtualKey);
            this.pressed = pressed;
            return Success;
        }

        public void Press() => pressed!();
        public void Dispose() => Disposals++;
    }
}

using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Win32;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class WindowTrackingTests
{
    [Fact]
    public void GenericForegroundChangesAreDeduplicatedAndIndependentOfSteam()
    {
        var native = new FakeWindowTrackingNative();
        using var monitor = new ForegroundWindowMonitor(native);
        using var tracker = new SteamWindowTracker(monitor);
        IForegroundWindowMonitor contract = monitor;
        var seen = new List<int?>();
        var steamEvents = 0;
        contract.ForegroundProcessChanged += (_, args) => seen.Add(args.ProcessId);
        tracker.SteamFocusGained += (_, _) => steamEvents++;
        tracker.SteamFocusLost += (_, _) => steamEvents++;
        monitor.Start();
        monitor.Start();

        foreach (var pid in new int?[] { 100, 100, 200, 100, null, null })
        {
            native.Foreground(pid);
            Assert.Equal(pid, contract.CurrentForegroundProcessId);
        }

        Assert.Equal(new int?[] { 100, 200, 100, null }, seen);
        Assert.Equal(0, steamEvents);
        Assert.Equal(1, native.Starts);
    }

    [Fact]
    public void SteamFocusEventsFireOnlyOnProcessTransitions()
    {
        var native = new FakeWindowTrackingNative();
        using var monitor = new ForegroundWindowMonitor(native);
        using var tracker = new SteamWindowTracker(monitor);
        var gained = 0;
        var lost = 0;
        tracker.SteamFocusGained += (_, _) => gained++;
        tracker.SteamFocusLost += (_, _) => lost++;
        monitor.Start();
        foreach (var pid in new[] { 42, 42, 100, 200, 42 })
            native.Foreground(pid);
        Assert.Equal(2, gained);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void BoundsAndDpiAreReadAgainOnEveryWindowChange()
    {
        var native = new FakeWindowTrackingNative();
        using var monitor = new ForegroundWindowMonitor(native);
        using var tracker = new SteamWindowTracker(monitor);
        var changes = new List<SteamWindowBoundsChangedEventArgs>();
        tracker.BoundsChanged += (_, args) => changes.Add(args);
        monitor.Start();

        native.Frame = new(-1920, -100, -100, 900);
        native.Dpi = 144;
        native.WindowChanged();
        native.Dpi = 192;
        native.WindowChanged();
        native.WindowChanged();

        Assert.Equal(4, native.DpiReads);
        Assert.Equal(3, changes.Count);
        Assert.Equal(new(-1920, -100, -100, 900, 144), changes[1]);
        Assert.Equal(192u, changes[2].Dpi);
    }

    [Fact]
    public void SteamAppearingClosingAndReopeningIsRediscovered()
    {
        var native = new FakeWindowTrackingNative();
        var window = native.Windows[0];
        native.Windows.Clear();
        using var monitor = new ForegroundWindowMonitor(native);
        using var tracker = new SteamWindowTracker(monitor);
        var boundsEvents = 0;
        tracker.BoundsChanged += (_, _) => boundsEvents++;
        monitor.Start();
        native.Windows.Add(window);
        native.WindowChanged();
        native.Frame = native.Monitor;
        native.WindowChanged();
        Assert.True(tracker.IsBigPictureMode);
        native.Windows.Clear();
        native.WindowChanged();
        Assert.False(tracker.IsBigPictureMode);
        native.Windows.Add(window with { Handle = 20, ProcessId = 43 });
        native.WindowChanged();
        Assert.Equal(3, boundsEvents);
    }

    [Theory]
    [InlineData("Enumerate")]
    [InlineData("Bounds")]
    [InlineData("Dpi")]
    [InlineData("Monitor")]
    public void AnyTrackerReadFailurePermanentlyDisablesOnlySteamTracking(string operation)
    {
        var native = new FakeWindowTrackingNative();
        using var monitor = new ForegroundWindowMonitor(native);
        using var tracker = new SteamWindowTracker(monitor);
        var events = 0;
        tracker.BoundsChanged += (_, _) => events++;
        tracker.SteamFocusGained += (_, _) => events++;
        tracker.SteamFocusLost += (_, _) => events++;
        monitor.Start();
        native.ThrowOn = operation;
        Assert.Null(Record.Exception(native.WindowChanged));
        var countAtFailure = events;
        var readsAtFailure = native.Enumerations;
        native.ThrowOn = null;
        native.Foreground(42);
        native.WindowChanged();

        Assert.Equal(countAtFailure, events);
        Assert.Equal(readsAtFailure, native.Enumerations);
        Assert.False(tracker.IsBigPictureMode);
        Assert.Equal(42, monitor.CurrentForegroundProcessId);
    }

    [Theory]
    [InlineData("Start")]
    [InlineData("ProcessId")]
    public void SharedHookStartupFailureLeavesForegroundUnknownWithoutRetry(string operation)
    {
        var native = new FakeWindowTrackingNative { ThrowOn = operation };
        using var monitor = new ForegroundWindowMonitor(native);
        using var tracker = new SteamWindowTracker(monitor);
        var events = 0;
        monitor.ForegroundProcessChanged += (_, _) => events++;
        tracker.BoundsChanged += (_, _) => events++;
        Assert.Null(Record.Exception(monitor.Start));
        native.ThrowOn = null;
        monitor.Start();
        native.Foreground(42);
        native.WindowChanged();

        Assert.Null(monitor.CurrentForegroundProcessId);
        Assert.Equal(0, events);
        Assert.Equal(1, native.Starts);
    }

    [Fact]
    public void SharedHookRuntimeFailureStopsBothSignals()
    {
        var native = new FakeWindowTrackingNative();
        using var monitor = new ForegroundWindowMonitor(native);
        using var tracker = new SteamWindowTracker(monitor);
        monitor.Start();
        native.Foreground(100);
        var events = 0;
        monitor.ForegroundProcessChanged += (_, _) => events++;
        tracker.BoundsChanged += (_, _) => events++;
        native.Fail();
        native.Foreground(42);
        native.WindowChanged();
        Assert.Null(monitor.CurrentForegroundProcessId);
        Assert.Equal(0, events);
    }

    [Theory]
    [InlineData("Bounds")]
    [InlineData("Gained")]
    [InlineData("Lost")]
    public void ThrowingSteamSubscriberCannotEscapeOrReceiveLaterEvents(string eventName)
    {
        var native = new FakeWindowTrackingNative();
        using var monitor = new ForegroundWindowMonitor(native);
        using var tracker = new SteamWindowTracker(monitor);
        var calls = 0;
        void Fail()
        {
            calls++;
            throw new InvalidOperationException("Subscriber failed.");
        }
        if (eventName == "Bounds") tracker.BoundsChanged += (_, _) => Fail();
        if (eventName == "Gained") tracker.SteamFocusGained += (_, _) => Fail();
        if (eventName == "Lost") tracker.SteamFocusLost += (_, _) => Fail();
        monitor.Start();
        native.Foreground(42);
        native.Foreground(100);
        native.Foreground(42);
        native.WindowChanged();
        Assert.Equal(1, calls);
        Assert.Equal(42, monitor.CurrentForegroundProcessId);
    }

    [Fact]
    public void ThrowingForegroundSubscriberDisablesTheSharedMonitorSilently()
    {
        var native = new FakeWindowTrackingNative();
        using var monitor = new ForegroundWindowMonitor(native);
        monitor.ForegroundProcessChanged += (_, _) => throw new InvalidOperationException();
        monitor.Start();
        Assert.Null(Record.Exception(() => native.Foreground(100)));
        Assert.Null(monitor.CurrentForegroundProcessId);
        native.Foreground(200);
        Assert.Null(monitor.CurrentForegroundProcessId);
    }

    [Fact]
    public void DisposalFailureDoesNotEscapeAndDisposedServicesStayStopped()
    {
        var native = new FakeWindowTrackingNative();
        var monitor = new ForegroundWindowMonitor(native);
        var tracker = new SteamWindowTracker(monitor);
        monitor.Start();
        tracker.Dispose();
        var reads = native.Enumerations;
        native.WindowChanged();
        Assert.Equal(reads, native.Enumerations);
        native.ThrowOn = "Dispose";
        Assert.Null(Record.Exception(monitor.Dispose));
        monitor.Dispose();
        monitor.Start();
        native.Foreground(42);
        Assert.Null(monitor.CurrentForegroundProcessId);
        Assert.Equal(1, native.Disposals);
        Assert.Equal(1, native.Starts);
    }
}

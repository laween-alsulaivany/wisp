using NSubstitute;
using Wisp.App.ViewModels;
using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class SteamButtonViewModelTests
{
    [Fact]
    public void Focus_bounds_dpi_hover_and_click_drive_the_button()
    {
        var tracker = Substitute.For<ISteamWindowTracker>();
        var picks = 0;
        using var model = new SteamButtonViewModel(tracker, action => action(), () => picks++);
        tracker.BoundsChanged += Raise.Event<EventHandler<SteamWindowBoundsChangedEventArgs>>(tracker, new SteamWindowBoundsChangedEventArgs(0, 0, 1920, 1080, 144));
        Assert.False(model.IsVisible);
        tracker.SteamFocusGained += Raise.Event<EventHandler>(tracker, EventArgs.Empty);
        Assert.True(model.IsVisible);
        Assert.Equal(66, model.Width);
        Assert.Equal(1836, model.Left);
        Assert.Equal(996, model.Top);
        model.IsHovered = true;
        Assert.Equal("Pick for me", model.Label);
        Assert.Equal(216, model.Width);
        Assert.Equal(1686, model.Left);
        model.PickCommand.Execute(null);
        Assert.Equal(1, picks);
        tracker.SteamFocusLost += Raise.Event<EventHandler>(tracker, EventArgs.Empty);
        Assert.False(model.IsVisible);
        Assert.False(model.IsHovered);
    }

    [Fact]
    public void Big_picture_is_hidden_and_invalid_bounds_disable_the_feature_for_the_session()
    {
        var tracker = Substitute.For<ISteamWindowTracker>();
        using var model = new SteamButtonViewModel(tracker, action => action(), () => { });
        tracker.SteamFocusGained += Raise.Event<EventHandler>(tracker, EventArgs.Empty);
        tracker.IsBigPictureMode.Returns(true);
        tracker.BoundsChanged += Raise.Event<EventHandler<SteamWindowBoundsChangedEventArgs>>(tracker, new SteamWindowBoundsChangedEventArgs(0, 0, 1920, 1080, 96));
        Assert.False(model.IsVisible);
        tracker.IsBigPictureMode.Returns(false);
        tracker.BoundsChanged += Raise.Event<EventHandler<SteamWindowBoundsChangedEventArgs>>(tracker, new SteamWindowBoundsChangedEventArgs(-1920, 0, 0, 1080, 96));
        Assert.True(model.IsVisible);
        Assert.Equal(-56, model.Left);
        tracker.BoundsChanged += Raise.Event<EventHandler<SteamWindowBoundsChangedEventArgs>>(tracker, new SteamWindowBoundsChangedEventArgs(0, 0, 0, 0, 0));
        tracker.BoundsChanged += Raise.Event<EventHandler<SteamWindowBoundsChangedEventArgs>>(tracker, new SteamWindowBoundsChangedEventArgs(0, 0, 1920, 1080, 96));
        Assert.False(model.IsVisible);
    }

    [Fact]
    public void Tracker_events_are_dispatched_and_disposal_ignores_queued_bounds()
    {
        var tracker = Substitute.For<ISteamWindowTracker>();
        var queue = new Queue<Action>();
        var model = new SteamButtonViewModel(tracker, queue.Enqueue, () => { });
        tracker.SteamFocusGained += Raise.Event<EventHandler>(tracker, EventArgs.Empty);
        tracker.BoundsChanged += Raise.Event<EventHandler<SteamWindowBoundsChangedEventArgs>>(tracker, new SteamWindowBoundsChangedEventArgs(0, 0, 1920, 1080, 96));
        Assert.False(model.IsVisible);
        model.Dispose();
        while (queue.TryDequeue(out var action)) action();
        Assert.False(model.IsVisible);
        tracker.SteamFocusLost += Raise.Event<EventHandler>(tracker, EventArgs.Empty);
        Assert.Empty(queue);
    }
}

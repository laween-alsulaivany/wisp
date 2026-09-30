using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wisp.App.ViewModels;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class HistoryViewModelTests
{
    private readonly ISessionRepository sessions = Substitute.For<ISessionRepository>();
    private readonly IFeedbackRepository feedback = Substitute.For<IFeedbackRepository>();
    private readonly IGameRepository games = Substitute.For<IGameRepository>();
    private readonly IClock clock = Substitute.For<IClock>();
    private readonly HistoryViewModel model;
    private readonly Feedback original = new()
    {
        FeedbackId = 123, SessionId = 31, GameId = 42, ProfileId = 7,
        FeedbackType = FeedbackType.Pending, IsPending = true
    };

    public HistoryViewModelTests()
    {
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
        var now = clock.UtcNow;
        sessions.GetRecentAsync(7, 100, default).Returns(new Session[]
        {
            new() { SessionId = 32, GameId = 42, ProfileId = 7, StartUtc = now },
            new() { SessionId = 31, GameId = 42, ProfileId = 7, StartUtc = now.AddHours(-1),
                EndUtc = now, RuntimeSeconds = 3600, ActiveForegroundSeconds = 1800 }
        });
        feedback.GetAllAsync(7, default).Returns(new[] { original });
        games.GetAllAsync(default).Returns(new Game[] { new() { GameId = 42, Name = "Game" } });
        model = new(sessions, feedback, games, clock, NullLogger<HistoryViewModel>.Instance);
    }

    [Fact]
    public async Task JoinsFeedbackBySessionIdAndDisplaysRuntimeActiveTimeAndPending()
    {
        await model.OpenAsync(7);
        Assert.Null(model.Entries[0].Feedback);
        var row = model.Entries[1];
        Assert.Equal(original, row.Feedback);
        Assert.Equal("Game", row.GameName);
        Assert.Contains("01:00:00", row.Display);
        Assert.Contains("00:30:00", row.Display);
        Assert.Contains("Pending", row.Display);
    }

    [Fact]
    public async Task EditUpsertsSelectedFeedbackWithSameIdentityAndNewAnswerAndTime()
    {
        await model.OpenAsync(7);
        model.Selected = model.Entries[1];
        model.SelectedFeedback = FeedbackType.Finished;
        await model.SaveFeedbackCommand.ExecuteAsync(null);
        await feedback.Received(1).RecordAsync(Arg.Is<Feedback>(f => f.FeedbackId == 123 && f.SessionId == 31 &&
            f.GameId == 42 && f.ProfileId == 7 && f.FeedbackType == FeedbackType.Finished && !f.IsPending &&
            f.RecordedUtc == clock.UtcNow), default);
        await feedback.DidNotReceive().RemoveAsync(Arg.Any<int>(), default);
    }

    [Fact]
    public async Task RemovePassesExactFeedbackIdWithoutDeletingSessionOrGame()
    {
        await model.OpenAsync(7);
        model.Selected = model.Entries[1];
        await model.RemoveFeedbackCommand.ExecuteAsync(null);
        await feedback.Received(1).RemoveAsync(123, default);
        await feedback.DidNotReceive().RecordAsync(Arg.Any<Feedback>(), default);
        await sessions.DidNotReceive().ClearHistoryAsync(Arg.Any<int>(), default);
        await games.DidNotReceive().UpsertAsync(Arg.Any<Game>(), default);
    }

    [Fact]
    public async Task OlderPendingFeedbackRemainsReachableOutsideRecentSessionLimit()
    {
        sessions.GetRecentAsync(7, 100, default).Returns(Array.Empty<Session>());
        sessions.GetByIdAsync(31, default).Returns(new Session { SessionId = 31, GameId = 42, ProfileId = 7 });
        await model.OpenAsync(7);
        Assert.Equal(original, Assert.Single(model.Entries).Feedback);
    }
}

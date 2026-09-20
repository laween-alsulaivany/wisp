using NSubstitute;
using Wisp.App.Services;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class GameStateServiceTests
{
    private readonly IFeedbackRepository feedback = Substitute.For<IFeedbackRepository>();
    private readonly IGameStateRepository states = Substitute.For<IGameStateRepository>();
    private readonly ISessionRepository sessions = Substitute.For<ISessionRepository>();
    private readonly ISettingsRepository settings = Substitute.For<ISettingsRepository>();
    private readonly FakeClock clock = new();
    private readonly Session session = new() { SessionId = 31, GameId = 42, ProfileId = 7 };
    private readonly GameStateService service;
    private GameState? stored;

    public GameStateServiceTests()
    {
        stored = new GameState
        {
            GameId = session.GameId, ProfileId = session.ProfileId, State = GameStateKind.Active,
            ActiveRankScore = 4.5, ConsecutiveKeepGoingCount = 3,
            StateChangedUtc = clock.UtcNow.AddDays(-2)
        };
        sessions.GetByIdAsync(session.SessionId, Arg.Any<CancellationToken>()).Returns(session);
        states.GetAsync(session.GameId, session.ProfileId, Arg.Any<CancellationToken>()).Returns(_ => stored);
        states.UpsertAsync(Arg.Any<GameState>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            stored = call.Arg<GameState>();
            return Task.CompletedTask;
        });
        settings.GetAsync(session.ProfileId, Arg.Any<CancellationToken>()).Returns(new ProfileSettings
        {
            ProfileId = session.ProfileId, MaybeLaterCooldownDays = 19
        });
        service = new GameStateService(feedback, states, sessions, settings, clock);
    }

    [Fact]
    public async Task KeepGoingActivatesBoostsRankAndClearsCooldown()
    {
        stored = stored! with { State = GameStateKind.MaybeLater, MaybeLaterUntilUtc = clock.UtcNow.AddDays(2) };
        await ApplyAsync(FeedbackType.KeepGoing);
        Assert.Equal(GameStateKind.Active, stored!.State);
        Assert.Equal(5.5, stored.ActiveRankScore);
        Assert.Equal(4, stored.ConsecutiveKeepGoingCount);
        Assert.Null(stored.MaybeLaterUntilUtc);
        Assert.Equal(clock.UtcNow, stored.StateChangedUtc);
    }

    [Fact]
    public async Task NotFeelingItUsesTheProfilesMaybeLaterCooldown()
    {
        await ApplyAsync(FeedbackType.NotFeelingIt);
        Assert.Equal(GameStateKind.MaybeLater, stored!.State);
        Assert.Equal(clock.UtcNow.AddDays(19), stored.MaybeLaterUntilUtc);
        Assert.Equal(4.5, stored.ActiveRankScore);
        Assert.Equal(0, stored.ConsecutiveKeepGoingCount);
        Assert.Equal(clock.UtcNow, stored.StateChangedUtc);
        await settings.Received(1).GetAsync(session.ProfileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public Task TechnicalIssueOnlyResetsTheStreak() => AssertNeutralAsync(FeedbackType.TechnicalIssue);

    [Fact]
    public Task InterruptedOnlyResetsTheStreak() => AssertNeutralAsync(FeedbackType.Interrupted);

    [Fact]
    public async Task MaybeLaterUsesTheProfilesCooldownFromTheCurrentClock()
    {
        clock.UtcNow = clock.UtcNow.AddDays(5);
        await ApplyAsync(FeedbackType.MaybeLater);
        Assert.Equal(GameStateKind.MaybeLater, stored!.State);
        Assert.Equal(clock.UtcNow.AddDays(19), stored.MaybeLaterUntilUtc);
        Assert.Equal(4.5, stored.ActiveRankScore);
        Assert.Equal(0, stored.ConsecutiveKeepGoingCount);
        Assert.Equal(clock.UtcNow, stored.StateChangedUtc);
        await settings.Received(1).GetAsync(session.ProfileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FinishedExplicitlyMarksFinishedAndClearsCooldown()
    {
        stored = stored! with { MaybeLaterUntilUtc = clock.UtcNow.AddDays(2) };
        await ApplyAsync(FeedbackType.Finished);
        Assert.Equal(GameStateKind.Finished, stored!.State);
        Assert.Equal(4.5, stored.ActiveRankScore);
        Assert.Equal(0, stored.ConsecutiveKeepGoingCount);
        Assert.Null(stored.MaybeLaterUntilUtc);
        Assert.Equal(clock.UtcNow, stored.StateChangedUtc);
    }

    [Fact]
    public async Task DroppedExplicitlyMarksDroppedAndClearsCooldown()
    {
        stored = stored! with { MaybeLaterUntilUtc = clock.UtcNow.AddDays(2) };
        await ApplyAsync(FeedbackType.Dropped);
        Assert.Equal(GameStateKind.Dropped, stored!.State);
        Assert.Equal(4.5, stored.ActiveRankScore);
        Assert.Equal(0, stored.ConsecutiveKeepGoingCount);
        Assert.Null(stored.MaybeLaterUntilUtc);
        Assert.Equal(clock.UtcNow, stored.StateChangedUtc);
    }

    [Fact]
    public async Task IgnoredFeedbackRecordsPendingWithoutAnyPreferenceChange()
    {
        stored = stored! with { State = GameStateKind.MaybeLater, MaybeLaterUntilUtc = clock.UtcNow.AddDays(2) };
        var before = stored;
        await ApplyAsync(FeedbackType.Pending);
        Assert.Same(before, stored);
        Assert.Empty(states.ReceivedCalls());
        Assert.Empty(settings.ReceivedCalls());
    }

    [Fact]
    public async Task KeepGoingIncreasesRankByOneAndStreakByOneAcrossThreeCalls()
    {
        for (var i = 1; i <= 3; i++)
        {
            var previousRank = stored!.ActiveRankScore;
            await service.ApplyFeedbackAsync(session.SessionId, FeedbackType.KeepGoing, default);
            Assert.Equal(previousRank + 1.0, stored!.ActiveRankScore);
            Assert.True(stored.ActiveRankScore > previousRank);
            Assert.Equal(3 + i, stored.ConsecutiveKeepGoingCount);
        }
    }

    [Theory]
    [InlineData(FeedbackType.NotFeelingIt)]
    [InlineData(FeedbackType.TechnicalIssue)]
    [InlineData(FeedbackType.Interrupted)]
    [InlineData(FeedbackType.MaybeLater)]
    [InlineData(FeedbackType.Finished)]
    [InlineData(FeedbackType.Dropped)]
    public async Task AnswerAfterConsecutiveKeepGoingResetsStreak(FeedbackType answer)
    {
        await service.ApplyFeedbackAsync(session.SessionId, FeedbackType.KeepGoing, default);
        await service.ApplyFeedbackAsync(session.SessionId, FeedbackType.KeepGoing, default);
        await service.ApplyFeedbackAsync(session.SessionId, answer, default);
        Assert.Equal(0, stored!.ConsecutiveKeepGoingCount);
        Assert.Equal(6.5, stored.ActiveRankScore);
    }

    [Fact]
    public async Task SessionIdentityIsResolvedBeforeStateAndFeedbackWrites()
    {
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        await service.ApplyFeedbackAsync(session.SessionId, FeedbackType.MaybeLater, ct);
        Received.InOrder(() =>
        {
            sessions.GetByIdAsync(session.SessionId, ct);
            states.GetAsync(session.GameId, session.ProfileId, ct);
            settings.GetAsync(session.ProfileId, ct);
            feedback.RecordAsync(Arg.Is<Feedback>(f => f.SessionId == session.SessionId &&
                f.GameId == session.GameId && f.ProfileId == session.ProfileId), ct);
            states.UpsertAsync(Arg.Is<GameState>(s => s.GameId == session.GameId && s.ProfileId == session.ProfileId), ct);
        });
    }

    [Fact]
    public async Task MissingSessionFailsWithoutWrites()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ApplyFeedbackAsync(999, FeedbackType.KeepGoing, default));
        Assert.Empty(feedback.ReceivedCalls());
        Assert.Empty(states.ReceivedCalls());
    }

    [Fact]
    public async Task FirstKeepGoingCreatesStateForTheSessionGameAndProfile()
    {
        stored = null;
        await service.ApplyFeedbackAsync(session.SessionId, FeedbackType.KeepGoing, default);
        Assert.Equal(new GameState
        {
            GameId = session.GameId, ProfileId = session.ProfileId, State = GameStateKind.Active,
            ActiveRankScore = 1, ConsecutiveKeepGoingCount = 1, StateChangedUtc = clock.UtcNow
        }, stored);
    }

    [Theory]
    [InlineData(FeedbackType.TechnicalIssue)]
    [InlineData(FeedbackType.Interrupted)]
    [InlineData(FeedbackType.Pending)]
    public async Task UnansweredOrNeutralFeedbackDoesNotCreateGameState(FeedbackType answer)
    {
        stored = null;
        await ApplyAsync(answer);
        Assert.Null(stored);
        await states.DidNotReceive().UpsertAsync(Arg.Any<GameState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingSettingsUsesProfileSettingsDefaultCooldown()
    {
        settings.GetAsync(session.ProfileId, Arg.Any<CancellationToken>()).Returns((ProfileSettings?)null);
        await service.ApplyFeedbackAsync(session.SessionId, FeedbackType.MaybeLater, default);
        Assert.Equal(clock.UtcNow.AddDays(new ProfileSettings().MaybeLaterCooldownDays), stored!.MaybeLaterUntilUtc);
    }

    [Theory]
    [InlineData(GameStateKind.NoData)]
    [InlineData(GameStateKind.Active)]
    [InlineData(GameStateKind.MaybeLater)]
    [InlineData(GameStateKind.Finished)]
    [InlineData(GameStateKind.Dropped)]
    public async Task ManualCorrectionAcceptsEveryStateIncludingFinished(GameStateKind target)
    {
        stored = stored! with { State = GameStateKind.Dropped, MaybeLaterUntilUtc = clock.UtcNow.AddDays(2) };
        await service.RestoreAsync(session.GameId, session.ProfileId, target, default);
        Assert.Equal(target, stored!.State);
        Assert.Equal(session.GameId, stored.GameId);
        Assert.Equal(session.ProfileId, stored.ProfileId);
        Assert.Equal(4.5, stored.ActiveRankScore);
        Assert.Equal(0, stored.ConsecutiveKeepGoingCount);
        Assert.Equal(clock.UtcNow, stored.StateChangedUtc);
        Assert.Equal(target == GameStateKind.MaybeLater ? clock.UtcNow.AddDays(19) : (DateTimeOffset?)null,
            stored.MaybeLaterUntilUtc);
        await states.Received(1).UpsertAsync(stored, Arg.Any<CancellationToken>());
        Assert.Empty(feedback.ReceivedCalls());
        Assert.Empty(sessions.ReceivedCalls());
    }

    [Fact]
    public async Task ManualCorrectionCanCreateFinishedStateWithoutASession()
    {
        stored = null;
        await service.RestoreAsync(session.GameId, session.ProfileId, GameStateKind.Finished, default);
        Assert.Equal(GameStateKind.Finished, stored!.State);
        Assert.Equal(0, stored.ActiveRankScore);
        Assert.Empty(sessions.ReceivedCalls());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(0.899999, false)]
    [InlineData(0.90, true)]
    [InlineData(0.95, true)]
    [InlineData(1.0, true)]
    public void CompletionHintOnlyReturnsAPromptDecisionIncludingAtTheExactBoundary(double? percentage, bool expected)
    {
        var before = stored;
        Assert.Equal(expected, CompletionHint.ShouldPrompt(percentage));
        Assert.Same(before, stored);
        Assert.Empty(states.ReceivedCalls());
        Assert.Empty(feedback.ReceivedCalls());
        Assert.Empty(sessions.ReceivedCalls());
        Assert.Empty(settings.ReceivedCalls());
    }

    [Fact]
    public async Task InvalidEnumsFailBeforeRepositoryCalls()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ApplyFeedbackAsync(session.SessionId, (FeedbackType)999, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.RestoreAsync(session.GameId, session.ProfileId, (GameStateKind)999, default));
        Assert.Empty(states.ReceivedCalls());
        Assert.Empty(feedback.ReceivedCalls());
        Assert.Empty(sessions.ReceivedCalls());
    }

    [Fact]
    public async Task CancelledOperationsFailBeforeRepositoryCalls()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ApplyFeedbackAsync(session.SessionId, FeedbackType.KeepGoing, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RestoreAsync(session.GameId, session.ProfileId, GameStateKind.Finished, cancellation.Token));
        Assert.Empty(states.ReceivedCalls());
        Assert.Empty(feedback.ReceivedCalls());
        Assert.Empty(sessions.ReceivedCalls());
    }

    private async Task AssertNeutralAsync(FeedbackType answer)
    {
        stored = stored! with { State = GameStateKind.MaybeLater, MaybeLaterUntilUtc = clock.UtcNow.AddDays(2) };
        var before = stored;
        await ApplyAsync(answer);
        Assert.Equal(before with { ConsecutiveKeepGoingCount = 0 }, stored);
        Assert.Empty(settings.ReceivedCalls());
    }

    private async Task ApplyAsync(FeedbackType answer)
    {
        await service.ApplyFeedbackAsync(session.SessionId, answer, default);
        await feedback.Received(1).RecordAsync(Arg.Is<Feedback>(f =>
            f.SessionId == session.SessionId && f.GameId == session.GameId && f.ProfileId == session.ProfileId &&
            f.FeedbackType == answer && f.IsPending == (answer == FeedbackType.Pending) &&
            f.RecordedUtc == (answer == FeedbackType.Pending ? null : clock.UtcNow)), Arg.Any<CancellationToken>());
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 20, 12, 30, 0, TimeSpan.Zero);
    }
}

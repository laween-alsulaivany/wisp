using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Xunit;

namespace Wisp.Core.Tests;

public sealed class EntityConstructionTests
{
    [Fact]
    public void GamesWithIdenticalFieldValuesAreEqual()
    {
        IReadOnlyList<string> tags = new[] { "Puzzle" };
        var lastPlayed = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var first = new Game
        {
            GameId = 1,
            AppId = 420,
            Name = "Example",
            Installed = true,
            SupportsController = true,
            Tags = tags,
            SteamCumulativePlaytimeMinutes = 120,
            SteamLastPlayedUtc = lastPlayed
        };
        var second = new Game
        {
            GameId = 1,
            AppId = 420,
            Name = "Example",
            Installed = true,
            SupportsController = true,
            Tags = tags,
            SteamCumulativePlaytimeMinutes = 120,
            SteamLastPlayedUtc = lastPlayed
        };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { AppId = 421 });
    }

    [Fact]
    public void GameDefaultsHaveEmptyMetadataAndNoPlaytime()
    {
        var game = new Game();

        Assert.Equal(string.Empty, game.Name);
        Assert.Empty(game.Tags);
        Assert.False(game.Installed);
        Assert.False(game.IsFreeToPlay);
        Assert.False(game.IsToolOrUtility);
        Assert.False(game.IsDemo);
        Assert.False(game.IsVrOnly);
        Assert.False(game.SupportsController);
        Assert.Equal(0, game.SteamCumulativePlaytimeMinutes);
        Assert.Null(game.SteamLastPlayedUtc);
    }

    [Fact]
    public void GameStatesWithIdenticalFieldValuesAreEqual()
    {
        var changed = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var first = new GameState
        {
            GameId = 1, ProfileId = 2, State = GameStateKind.MaybeLater,
            ActiveRankScore = 3.5, MaybeLaterUntilUtc = changed.AddDays(7), StateChangedUtc = changed
        };
        var second = new GameState
        {
            GameId = 1, ProfileId = 2, State = GameStateKind.MaybeLater,
            ActiveRankScore = 3.5, MaybeLaterUntilUtc = changed.AddDays(7), StateChangedUtc = changed
        };

        Assert.Equal(first, second);
        Assert.NotEqual(first, second with { ProfileId = 3 });
    }

    [Fact]
    public void SessionsWithIdenticalFieldValuesAreEqual()
    {
        var start = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var first = new Session
        {
            SessionId = 1, GameId = 2, ProfileId = 3, StartUtc = start,
            EndUtc = start.AddHours(1), RuntimeSeconds = 3600,
            ActiveForegroundSeconds = 2400, RestartCount = 1, LaunchSource = LaunchSource.Recommendation
        };
        var second = new Session
        {
            SessionId = 1, GameId = 2, ProfileId = 3, StartUtc = start,
            EndUtc = start.AddHours(1), RuntimeSeconds = 3600,
            ActiveForegroundSeconds = 2400, RestartCount = 1, LaunchSource = LaunchSource.Recommendation
        };

        Assert.Equal(first, second);
        Assert.NotEqual(first, second with { RuntimeSeconds = 7200 });
    }

    [Fact]
    public void SessionCanBeConstructedWithoutAnEndOrRuntime()
    {
        var session = new Session { GameId = 1, ProfileId = 2 };

        Assert.Null(session.EndUtc);
        Assert.Null(session.RuntimeSeconds);
        Assert.Equal(0, session.ActiveForegroundSeconds);
        Assert.Equal(0, session.RestartCount);
    }

    [Fact]
    public void TimeFilterSupportsAnyAndTargetDurationValues()
    {
        Assert.Null(default(TimeFilter).TargetDuration);
        Assert.Equal(default, new TimeFilter(null));
        Assert.Equal(new TimeFilter(TimeSpan.FromMinutes(30)), new TimeFilter(TimeSpan.FromMinutes(30)));
        Assert.NotEqual(new TimeFilter(null), new TimeFilter(TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void RecommendationRequestDefaultsToUnrestrictedFilters()
    {
        var request = new RecommendationRequest { ProfileId = 7 };

        Assert.Equal(7, request.ProfileId);
        Assert.Equal(MoodFilter.Anything, request.Mood);
        Assert.Null(request.Time.TargetDuration);
        Assert.Equal(AdvancedFilters.None, request.Advanced);
        Assert.Empty(request.ExcludedGameIdsThisSession);
    }

    [Fact]
    public void RecommendationRequestsWithIdenticalFieldValuesAreEqual()
    {
        IReadOnlySet<int> excluded = new HashSet<int> { 1, 2 };
        var advanced = new AdvancedFilters { ControllerFriendlyOnly = true };
        var first = new RecommendationRequest
        {
            ProfileId = 7, Mood = MoodFilter.LowEnergy, Time = new(TimeSpan.FromMinutes(30)),
            Advanced = advanced, ExcludedGameIdsThisSession = excluded
        };
        var second = new RecommendationRequest
        {
            ProfileId = 7, Mood = MoodFilter.LowEnergy, Time = new(TimeSpan.FromMinutes(30)),
            Advanced = advanced, ExcludedGameIdsThisSession = excluded
        };

        Assert.Equal(first, second);
        Assert.NotEqual(first, second with { Mood = MoodFilter.HighEnergy });
    }

    [Fact]
    public void RecommendationResultsWithIdenticalFieldValuesAreEqual()
    {
        IReadOnlyList<string> reasons = new[] { "Example reason" };
        var first = new RecommendationResult
        {
            Game = new Game { GameId = 1 }, Reasons = reasons, Score = 0.8, FallbackTierUsed = 2
        };
        var second = new RecommendationResult
        {
            Game = new Game { GameId = 1 }, Reasons = reasons, Score = 0.8, FallbackTierUsed = 2
        };

        Assert.Equal(first, second);
        Assert.Empty(new RecommendationResult { Game = first.Game }.Reasons);
        Assert.NotEqual(first, second with { FallbackTierUsed = 3 });
    }

    [Fact]
    public void FeedbackSupportsPendingAndRecordedValues()
    {
        var pending = new Feedback
        {
            FeedbackId = 1, SessionId = 2, GameId = 3, ProfileId = 4,
            FeedbackType = FeedbackType.Pending, IsPending = true
        };
        var identical = new Feedback
        {
            FeedbackId = 1, SessionId = 2, GameId = 3, ProfileId = 4,
            FeedbackType = FeedbackType.Pending, IsPending = true
        };
        var recorded = pending with
        {
            FeedbackType = FeedbackType.KeepGoing, IsPending = false, Edited = true,
            RecordedUtc = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)
        };

        Assert.Equal(pending, identical);
        Assert.Null(pending.RecordedUtc);
        Assert.NotEqual(pending, recorded);
        Assert.True(recorded.Edited);
        Assert.NotNull(recorded.RecordedUtc);
    }
}

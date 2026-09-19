using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Xunit;

namespace Wisp.Core.Tests;

public sealed class SupportingRecordConstructionTests
{
    [Fact]
    public void InstalledManifestsWithIdenticalFieldsAreEqual()
    {
        var first = new InstalledGameManifest(420, "Example", true, @"C:\Steam\steamapps\common\Example");
        var second = new InstalledGameManifest(420, "Example", true, @"C:\Steam\steamapps\common\Example");

        Assert.Equal(first, second);
        Assert.NotEqual(first, second with { Installed = false });
    }

    [Fact]
    public void PlaytimeRecordsAllowUnknownLastPlayedTime()
    {
        Assert.Equal(new PlaytimeRecord(120, null), new PlaytimeRecord(120, null));
        Assert.Null(new PlaytimeRecord(0, null).SteamLastPlayedUtc);
    }

    [Fact]
    public void AppInfoRecordsPreserveMetadataFields()
    {
        IReadOnlyList<int> tags = new[] { 10, 20 };
        IReadOnlyList<int> genres = new[] { 1 };
        var first = new AppInfoRecord
        {
            AppId = 420, IsFreeToPlay = true, IsToolOrUtility = true, IsDemo = true,
            IsVrOnly = true, SupportsController = true, IsSinglePlayer = true,
            IsMultiplayer = true, IsStoryFocused = true, TagIds = tags, GenreIds = genres
        };
        var second = new AppInfoRecord
        {
            AppId = 420, IsFreeToPlay = true, IsToolOrUtility = true, IsDemo = true,
            IsVrOnly = true, SupportsController = true, IsSinglePlayer = true,
            IsMultiplayer = true, IsStoryFocused = true, TagIds = tags, GenreIds = genres
        };

        Assert.Equal(first, second);
        Assert.Empty(new AppInfoRecord().TagIds);
        Assert.Empty(new AppInfoRecord().GenreIds);
    }

    [Fact]
    public void EligibilityFiltersPreserveTheTimeAndInclusionFlags()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var first = new EligibilityFilter
        {
            UtcNow = now, IncludeFinishedGames = true, IncludeDemos = true, IncludeVrOnly = true
        };
        var second = new EligibilityFilter
        {
            UtcNow = now, IncludeFinishedGames = true, IncludeDemos = true, IncludeVrOnly = true
        };

        Assert.Equal(first, second);
        Assert.NotEqual(first, second with { UtcNow = now.AddDays(1) });
    }

    [Fact]
    public void AdvancedFiltersNoneHasNoRestrictionsOrInclusionOverrides()
    {
        Assert.Equal(new AdvancedFilters(), AdvancedFilters.None);
        Assert.False(AdvancedFilters.None.StoryFocusedOnly);
        Assert.False(AdvancedFilters.None.SinglePlayerOnly);
        Assert.False(AdvancedFilters.None.MultiplayerOnly);
        Assert.False(AdvancedFilters.None.ControllerFriendlyOnly);
        Assert.Empty(AdvancedFilters.None.GenreIds);
        Assert.Empty(AdvancedFilters.None.Tags);
        Assert.False(AdvancedFilters.None.IncludeFinishedGames);
        Assert.False(AdvancedFilters.None.IncludeDemos);
        Assert.False(AdvancedFilters.None.IncludeVrOnly);
    }

    [Fact]
    public void PlaytimeDistributionPreservesUnprocessedSamples()
    {
        IReadOnlyList<int> samples = new[] { 900, 1800, 10800 };
        var distribution = new PlaytimeDistribution { ActiveForegroundSeconds = samples };

        Assert.Equal(distribution, new PlaytimeDistribution { ActiveForegroundSeconds = samples });
        Assert.Same(samples, distribution.ActiveForegroundSeconds);
        Assert.Empty(new PlaytimeDistribution().ActiveForegroundSeconds);
    }

    [Fact]
    public void CompletionEstimatesAllowMissingDurations()
    {
        var first = new CompletionEstimate(420, 120, null, null, "2026-09");
        var second = new CompletionEstimate(420, 120, null, null, "2026-09");

        Assert.Equal(first, second);
        Assert.Null(first.MainPlusExtraMinutes);
        Assert.Null(first.CompletionistMinutes);
        Assert.NotEqual(first, second with { SourceVersion = "2026-10" });
    }

    [Fact]
    public void SessionEventsCarrySessionValues()
    {
        var first = new Session { SessionId = 1, GameId = 2, ProfileId = 3 };
        var second = new Session { SessionId = 1, GameId = 2, ProfileId = 3 };

        Assert.Equal(new SessionStartedEventArgs(first), new SessionStartedEventArgs(second));
        Assert.Equal(new SessionEndedEventArgs(first), new SessionEndedEventArgs(second));
        Assert.Same(first, new SessionStartedEventArgs(first).Session);
        Assert.Same(first, new SessionEndedEventArgs(first).Session);
    }

    [Fact]
    public void WindowBoundsSupportNegativeScreenCoordinatesAndDpi()
    {
        var first = new SteamWindowBoundsChangedEventArgs(-1920, -100, 0, 980, 144);
        var second = new SteamWindowBoundsChangedEventArgs(-1920, -100, 0, 980, 144);

        Assert.Equal(first, second);
        Assert.Equal(-1920, first.Left);
        Assert.Equal(144u, first.Dpi);
        Assert.NotEqual(first, second with { Dpi = 192 });
    }

    [Fact]
    public void RecommendationContextsWithIdenticalFieldsAreEqual()
    {
        var first = new GameRecommendationContext
        {
            Game = new Game { GameId = 1 }, State = new GameState { GameId = 1 },
            RankComponent = 0.9, TimeComponent = 0.7, DiscoveryBoost = 0,
            ConsecutiveKeepGoingCount = 3, DecayedRankPercentile = 90, StoredRankPercentile = 95,
            DaysSinceLastSession = 100, LoggedSessionCount = 4, EligiblePoolSize = 3
        };
        var second = new GameRecommendationContext
        {
            Game = new Game { GameId = 1 }, State = new GameState { GameId = 1 },
            RankComponent = 0.9, TimeComponent = 0.7, DiscoveryBoost = 0,
            ConsecutiveKeepGoingCount = 3, DecayedRankPercentile = 90, StoredRankPercentile = 95,
            DaysSinceLastSession = 100, LoggedSessionCount = 4, EligiblePoolSize = 3
        };

        Assert.Equal(first, second);
        Assert.Null(new GameRecommendationContext { Game = first.Game, State = first.State }.DaysSinceLastSession);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "https://example.com/releases/v2")]
    public void UpdateResultsPreserveAvailabilityAndReleaseUrl(bool available, string? releaseUrl)
    {
        var result = new UpdateCheckResult(available, releaseUrl);

        Assert.Equal(new UpdateCheckResult(available, releaseUrl), result);
        Assert.Equal(available, result.UpdateAvailable);
        Assert.Equal(releaseUrl, result.ReleasePageUrl);
    }
}

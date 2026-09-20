using Wisp.Core.Enums;
using Xunit;
using static Wisp.RecommendationEngine.Tests.RecommendationFixtures;

namespace Wisp.RecommendationEngine.Tests;

public sealed class RecommendationPoolTests
{
    [Theory]
    [InlineData(MoodFilter.LowEnergy, "Relaxing|Casual|Walking Simulator|Puzzle|Cozy|Atmospheric|Point & Click")]
    [InlineData(MoodFilter.Normal, "Adventure|RPG|Simulation|Strategy|Story Rich|Open World")]
    [InlineData(MoodFilter.HighEnergy, "Action|FPS|Fast-Paced|Roguelike|Souls-like|Competitive|Hack and Slash")]
    public void EveryAllowlistedTagMatchesItsMood(MoodFilter mood, string tags)
    {
        foreach (var tag in tags.Split('|'))
            Assert.True(RecommendationPool.MatchesMood(Candidate(1, tag: tag.ToLowerInvariant()), mood));
        Assert.False(RecommendationPool.MatchesMood(Candidate(1, tag: "Unknown"), mood));
    }

    [Fact]
    public void AnythingMatchesWithoutTags()
    {
        var candidate = Candidate(1);
        candidate = candidate with { Game = candidate.Game with { Tags = [] } };
        Assert.True(RecommendationPool.MatchesMood(candidate, MoodFilter.Anything));
    }

    [Theory]
    [InlineData(1, 30, true)]
    [InlineData(1, 90, true)]
    [InlineData(1, 90.01, false)]
    [InlineData(1, 29.99, false)]
    [InlineData(2, 150, true)]
    [InlineData(2, 150.01, false)]
    [InlineData(3, 1000, true)]
    public void TimeCutoffsAreInclusiveAndTierThreeIgnoresTime(int tier, double minutes, bool included)
    {
        var snapshot = Snapshot(Candidate(1, minutes));
        Assert.Equal(included, RecommendationPool.ForTier(Request(60), snapshot, tier).Length == 1);
    }

    [Fact]
    public void UnknownTimeAndAnyTimeAreNeutral()
    {
        Assert.Single(RecommendationPool.ForTier(Request(60), Snapshot(Candidate(1)), 1));
        Assert.Single(RecommendationPool.ForTier(Request(), Snapshot(Candidate(1, 1000)), 1));
    }

    [Fact]
    public void InclusionUsesTheSameDurationFallbackAsScoring()
    {
        var candidate = Candidate(1, 600) with { LoggedSessionCount = 2, CompletionEstimateMainStoryMinutes = 60 };
        var snapshot = Snapshot(candidate) with { ProfileWideMedianSessionMinutes = 120 };
        Assert.Empty(RecommendationPool.ForTier(Request(60), snapshot, 1));
        Assert.Single(RecommendationPool.ForTier(Request(60), snapshot, 2));
        Assert.Single(RecommendationPool.ForTier(Request(60), snapshot with { ProfileWideMedianSessionMinutes = null }, 1));
    }

    [Fact]
    public void TerminalTiersPreserveTheProvidedBasePool()
    {
        var snapshot = Snapshot(Candidate(1, 1000, "Unknown") with { State = GameStateKind.Finished });
        Assert.Equal(snapshot.Candidates, RecommendationPool.ForTier(Request(60, MoodFilter.LowEnergy), snapshot, 4));
        Assert.Equal(snapshot.Candidates, RecommendationPool.ForTier(Request(60, MoodFilter.LowEnergy), snapshot, 5));
    }
}

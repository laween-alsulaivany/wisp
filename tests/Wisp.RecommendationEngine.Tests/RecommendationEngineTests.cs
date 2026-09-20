using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;
using Xunit;
using static Wisp.RecommendationEngine.Tests.RecommendationFixtures;

namespace Wisp.RecommendationEngine.Tests;

public sealed class RecommendationEngineTests
{
    [Theory]
    [InlineData(60, "Puzzle", 1)]
    [InlineData(120, "Puzzle", 2)]
    [InlineData(180, "Puzzle", 3)]
    [InlineData(60, "Action", 4)]
    public async Task UsesFirstQualifyingTier(double minutes, string tag, int expectedTier)
    {
        var snapshot = Snapshot(Candidate(1, minutes, tag), Candidate(2, minutes, tag), Candidate(3, minutes, tag));
        var result = await new RecommendationEngine(new Random(17))
            .GetRecommendationAsync(Request(60, MoodFilter.LowEnergy), snapshot, default);

        Assert.NotNull(result);
        Assert.Equal(expectedTier, result.FallbackTierUsed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SmallLibraryAlwaysUsesTierFive(int size)
    {
        var snapshot = Snapshot(Enumerable.Range(1, size).Select(id => Candidate(id, 60)).ToArray());
        var result = await new RecommendationEngine(new Random(17))
            .GetRecommendationAsync(Request(60, MoodFilter.LowEnergy), snapshot, default);

        Assert.NotNull(result);
        Assert.Equal(5, result.FallbackTierUsed);
        Assert.Contains(result.Game, snapshot.Candidates.Select(candidate => candidate.Game));
    }

    [Fact]
    public async Task EmptyBasePoolReturnsNull()
    {
        IRecommendationEngine engine = new RecommendationEngine();
        Assert.Null(await engine.GetRecommendationAsync(Request(), Snapshot(), default));
    }

    [Fact]
    public async Task LooseTimeCandidateEntersOnlyWhenTierTwoIsReached()
    {
        var request = Request(60, MoodFilter.LowEnergy);
        var loose = Candidate(4, 120);
        var snapshot = Snapshot(Candidate(1, 60), Candidate(2, 60), Candidate(3, 60), loose);
        Assert.DoesNotContain(loose, RecommendationPool.ForTier(request, snapshot, 1));
        Assert.Contains(loose, RecommendationPool.ForTier(request, snapshot, 2));
        var engine = new RecommendationEngine(new Random(12));
        for (var trial = 0; trial < 30; trial++)
        {
            var result = await engine.GetRecommendationAsync(request, snapshot, default);
            Assert.NotNull(result);
            Assert.Equal(1, result.FallbackTierUsed);
            Assert.NotEqual(4, result.Game.GameId);
        }

        var fallback = snapshot with { Candidates = [Candidate(1, 60), Candidate(2, 60), loose] };
        var seen = new HashSet<int>();
        for (var trial = 0; trial < 100; trial++)
        {
            var result = await engine.GetRecommendationAsync(request, fallback, default);
            Assert.NotNull(result);
            Assert.Equal(2, result.FallbackTierUsed);
            seen.Add(result.Game.GameId);
        }
        Assert.Contains(4, seen);
    }

    [Fact]
    public async Task RerollExhaustsEachTierBeforeWideningAndAllGamesBeforeRepeating()
    {
        var candidates = new[]
        {
            Candidate(1, 60), Candidate(2, 60), Candidate(3, 60),
            Candidate(4, 120), Candidate(5, 180), Candidate(6, 60, "Action")
        };
        var snapshot = Snapshot(candidates);
        var excluded = new HashSet<int>();
        var request = Request(60, MoodFilter.LowEnergy) with { ExcludedGameIdsThisSession = excluded };
        var engine = new RecommendationEngine(new Random(41));
        int[] expectedTiers = [1, 1, 1, 2, 3, 4];
        foreach (var tier in expectedTiers)
        {
            var before = excluded.ToArray();
            var result = await engine.GetRecommendationAsync(request, snapshot, default);
            Assert.NotNull(result);
            Assert.Equal(tier, result.FallbackTierUsed);
            Assert.Equal(before, excluded.ToArray());
            Assert.True(excluded.Add(result.Game.GameId));
        }

        for (var trial = 0; trial < 20; trial++)
        {
            var repeat = await engine.GetRecommendationAsync(request, snapshot, default);
            Assert.NotNull(repeat);
            Assert.Equal(5, repeat.FallbackTierUsed);
            Assert.Contains(repeat.Game.GameId, excluded);
        }
        Assert.Equal(candidates, snapshot.Candidates);
        var reopened = await engine.GetRecommendationAsync(Request(60, MoodFilter.LowEnergy), snapshot, default);
        Assert.NotNull(reopened);
        Assert.Equal(1, reopened.FallbackTierUsed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SmallLibraryRerollReadmitsOnlyAfterExhaustion(int size)
    {
        var snapshot = Snapshot(Enumerable.Range(1, size).Select(id => Candidate(id)).ToArray());
        var excluded = new HashSet<int>();
        var request = Request() with { ExcludedGameIdsThisSession = excluded };
        var engine = new RecommendationEngine(new Random(9));
        for (var index = 0; index < size; index++)
        {
            var result = await engine.GetRecommendationAsync(request, snapshot, default);
            Assert.NotNull(result);
            Assert.Equal(5, result.FallbackTierUsed);
            Assert.True(excluded.Add(result.Game.GameId));
        }
        Assert.NotNull(await engine.GetRecommendationAsync(request, snapshot, default));
    }

    [Fact]
    public async Task ResultCarriesSelectedScoreAndReasons()
    {
        var candidate = Candidate(1) with { NeverPlayed = true, ConsecutiveKeepGoingStreak = 3 };
        var result = await new RecommendationEngine().GetRecommendationAsync(Request(), Snapshot(candidate), default);
        Assert.NotNull(result);
        Assert.Same(candidate.Game, result.Game);
        Assert.Equal(0.695, result.Score, 10);
        Assert.Equal(["You marked this 'Keep going' after your last 3 sessions.", "You haven't tried this one yet."], result.Reasons);
    }

    [Fact]
    public async Task CancellationIsHonoredEvenForEmptyPool()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RecommendationEngine()
            .GetRecommendationAsync(Request(), Snapshot(), new CancellationToken(true)));
    }
}

using Wisp.Core.Enums;
using Xunit;
using static Wisp.RecommendationEngine.Tests.RecommendationFixtures;

namespace Wisp.RecommendationEngine.Tests;

public sealed class RecommendationReasonsTests
{
    [Fact]
    public void SpecificReasonsWinAndStreakAlwaysAppearsInTopTwo()
    {
        var candidate = Active(1, 100, 120) with
        {
            ConsecutiveKeepGoingStreak = 4, LastSessionUtc = Now.AddDays(-120), NeverPlayed = true
        };
        var snapshot = Snapshot(candidate);
        var scored = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        var context = RecommendationReasons.Context(scored[0], scored, Now);
        Assert.True(RecommendationReasons.KeepGoingStreakRule.Matches(context));
        Assert.True(RecommendationReasons.LongAbsenceHighRankRule.Matches(context));
        Assert.True(RecommendationReasons.TopRankedActiveRule.Matches(context));
        Assert.True(RecommendationReasons.DiscoveryRule.Matches(context));
        Assert.Equal(new[]
        {
            "You marked this 'Keep going' after your last 4 sessions.",
            "You haven't returned to this in 4 months despite strong previous feedback."
        }, RecommendationReasons.Generate(context));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void StreakThresholdIsThree(int streak, bool matches)
    {
        var context = Context() with
        {
            Score = Context().Score with { Candidate = Candidate(1) with { ConsecutiveKeepGoingStreak = streak } }
        };
        Assert.Equal(matches, RecommendationReasons.KeepGoingStreakRule.Matches(context));
    }

    [Theory]
    [InlineData(GameStateKind.Active, 84.99, false)]
    [InlineData(GameStateKind.Active, 85, true)]
    [InlineData(GameStateKind.NoData, 100, false)]
    public void TopRankRequiresActiveAndEightyFifthPercentile(GameStateKind state, double percentile, bool matches)
    {
        var context = Context(state) with { DecayedRankPercentile = percentile };
        Assert.Equal(matches, RecommendationReasons.TopRankedActiveRule.Matches(context));
    }

    [Theory]
    [InlineData(GameStateKind.Active, 90.0, 75, true)]
    [InlineData(GameStateKind.Active, 89.99, 100, false)]
    [InlineData(GameStateKind.Active, 90.0, 74.99, false)]
    [InlineData(GameStateKind.Active, null, 100, false)]
    [InlineData(GameStateKind.Finished, 120.0, 100, false)]
    public void AbsenceRequiresNinetyDaysAndTopQuartileStoredRank(
        GameStateKind state, double? days, double percentile, bool matches)
    {
        var context = Context(state) with { DaysSinceLastSession = days, StoredRankPercentile = percentile };
        Assert.Equal(matches, RecommendationReasons.LongAbsenceHighRankRule.Matches(context));
    }

    [Theory]
    [InlineData(true, 4, true)]
    [InlineData(true, 5, false)]
    [InlineData(false, 4, false)]
    public void DiscoveryRequiresNeverPlayedAndThinPool(bool neverPlayed, int poolSize, bool matches)
    {
        var context = Context() with
        {
            Score = Context().Score with { Candidate = Candidate(1) with { NeverPlayed = neverPlayed } },
            PoolSize = poolSize
        };
        Assert.Equal(matches, RecommendationReasons.DiscoveryRule.Matches(context));
    }

    [Fact]
    public void PercentilesUseActivePoolAndPreDecayRankForAbsence()
    {
        var snapshot = Snapshot(Active(1, 1000, 450) with { LastSessionUtc = Now.AddDays(-120) },
            Active(2, 20), Active(3, 30), Active(4, 40), Candidate(5));
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        var context = RecommendationReasons.Context(scores[0], scores, Now);
        Assert.Equal(25, context.DecayedRankPercentile);
        Assert.Equal(100, context.StoredRankPercentile);
        Assert.Equal(120, context.DaysSinceLastSession);
        Assert.Equal(new[] { "You haven't returned to this in 4 months despite strong previous feedback." },
            RecommendationReasons.Generate(context));
    }

    [Fact]
    public void TiedAndSingletonActiveCandidatesHaveEqualTopPercentiles()
    {
        foreach (var size in new[] { 1, 3 })
        {
            var snapshot = Snapshot(Enumerable.Range(1, size).Select(id => Active(id, 10)).ToArray());
            var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
            foreach (var score in scores)
            {
                var context = RecommendationReasons.Context(score, scores, Now);
                Assert.Equal(100, context.DecayedRankPercentile);
                Assert.Equal(100, context.StoredRankPercentile);
                Assert.Equal(new[] { "This is currently one of your highest-ranked active games." },
                    RecommendationReasons.Generate(context));
            }
        }
    }

    [Fact]
    public void NoMatchingRuleReturnsNoGenericOrObviousReason()
    {
        var candidate = Candidate(1);
        candidate = candidate with { Game = candidate.Game with { IsSinglePlayer = true, SupportsController = true } };
        var snapshot = Snapshot(candidate);
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        var context = RecommendationReasons.Context(scores[0], scores, Now);
        Assert.Equal(0, context.DecayedRankPercentile);
        Assert.Equal(0, context.StoredRankPercentile);
        Assert.Empty(RecommendationReasons.Generate(context));
    }

    [Fact]
    public void GeneratedReasonsAreExclusivelyTheFourNamedTemplates()
    {
        var snapshot = Snapshot(Active(1, 100) with { ConsecutiveKeepGoingStreak = 3 },
            Active(2, 500, 120) with { LastSessionUtc = Now.AddDays(-120) },
            Candidate(3) with { NeverPlayed = true });
        string[] allowed =
        [
            "You marked this 'Keep going' after your last 3 sessions.",
            "You haven't returned to this in 4 months despite strong previous feedback.",
            "This is currently one of your highest-ranked active games.",
            "You haven't tried this one yet."
        ];
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        var reasons = scores.SelectMany(score => RecommendationReasons.Generate(
            RecommendationReasons.Context(score, scores, Now))).ToArray();
        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason => Assert.Contains(reason, allowed));
        Assert.DoesNotContain(reasons, reason => reason.Contains("Installed") ||
            reason.Contains("Owned on Steam") || reason.Contains("Single-player"));
    }

    private static GameRecommendationContext Context(GameStateKind state = GameStateKind.NoData)
    {
        var candidate = Candidate(1) with { State = state };
        return new GameRecommendationContext(new ScoredCandidate(candidate, 0, 0.3, 1, 0, 0.545), 0, 0, null, 1);
    }
}

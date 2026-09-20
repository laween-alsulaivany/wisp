using Wisp.Core.Enums;
using Xunit;
using static Wisp.RecommendationEngine.Tests.RecommendationFixtures;

namespace Wisp.RecommendationEngine.Tests;

public sealed class RecommendationScoringTests
{
    [Fact]
    public void RawRankDecaysMonotonicallyWithTheSpecifiedHalfLife()
    {
        var candidate = Active(1, 100);
        var previous = double.PositiveInfinity;
        foreach (var days in new[] { 0, 15, 45, 90, 120, 365 })
        {
            var rank = RecommendationScoring.DecayedRank(candidate, Now.AddDays(days));
            Assert.Equal(100 * Math.Exp(-Math.Log(2) / 45 * days), rank, 10);
            Assert.True(rank < previous);
            previous = rank;
        }
        Assert.Equal(50, RecommendationScoring.DecayedRank(candidate, Now.AddDays(45)), 10);
    }

    [Fact]
    public void FourMonthHighRankStillOutscoresNoDataOnTotalScore()
    {
        var snapshot = Snapshot(Active(1, 1000, 120), Active(2, 20), Candidate(3));
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        Assert.True(scores[0].TotalScore > scores[2].TotalScore);
        Assert.Equal(1, scores[0].TotalScore, 10);
        Assert.Equal(0.545, scores[2].TotalScore, 10);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SingletonAndIdenticalActiveRanksNormalizeToOne(int count)
    {
        var snapshot = Snapshot(Enumerable.Range(1, count).Select(id => Active(id, 20, 45)).ToArray());
        Assert.All(RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot), score =>
        {
            Assert.Equal(1, score.RankComponent);
            Assert.True(double.IsFinite(score.TotalScore));
        });
    }

    [Theory]
    [InlineData(GameStateKind.NoData)]
    [InlineData(GameStateKind.Finished)]
    [InlineData(GameStateKind.MaybeLater)]
    public void EligibleNonActiveStatesShareTheSameBaseline(GameStateKind state)
    {
        var candidate = Candidate(1) with { State = state, MaybeLaterUntilUtc = Now.AddDays(-1) };
        var snapshot = Snapshot(candidate, Candidate(2));
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        Assert.Equal(0.30, scores[0].RankComponent);
        Assert.Equal(scores[1].RankComponent, scores[0].RankComponent);
        Assert.Equal(scores[1].TotalScore, scores[0].TotalScore);
    }

    [Fact]
    public void NormalizationUsesOnlyActiveCandidatesInTheChosenPool()
    {
        var snapshot = Snapshot(Active(1, 10), Active(2, 20), Active(3, 30), Candidate(4));
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        Assert.Equal(new[] { 0, 0.5, 1, 0.3 }, scores.Select(score => score.RankComponent));
    }

    [Theory]
    [InlineData(3, 75.0, 120.0, 200, 75.0)]
    [InlineData(3, null, 120.0, 200, 120.0)]
    [InlineData(2, 75.0, 120.0, 200, 120.0)]
    [InlineData(3, null, null, 200, 200.0)]
    [InlineData(3, null, null, null, null)]
    public void ExpectedTimeUsesPersonalMedianThenProfileMedianThenCompletion(
        int count, double? personal, double? profile, int? completion, double? expected)
    {
        var candidate = Candidate(1) with
        {
            LoggedSessionCount = count, PersonalMedianSessionMinutes = personal,
            CompletionEstimateMainStoryMinutes = completion
        };
        var snapshot = Snapshot(candidate) with { ProfileWideMedianSessionMinutes = profile };
        Assert.Equal(expected, RecommendationScoring.ExpectedMinutes(candidate, snapshot));
        var expectedComponent = expected is null ? 1 : Math.Clamp(1 - Math.Abs(expected.Value - 60) / 90, 0.2, 1);
        Assert.Equal(expectedComponent, RecommendationScoring.TimeComponent(candidate, Request(60), snapshot), 10);
    }

    [Theory]
    [InlineData(60, 1)]
    [InlineData(15, 0.5)]
    [InlineData(105, 0.5)]
    [InlineData(150, 0.2)]
    [InlineData(500, 0.2)]
    public void TimeScoreUsesAbsoluteDistanceAndClampsAtFloor(double expected, double component)
    {
        var candidate = Candidate(1, expected);
        Assert.Equal(component, RecommendationScoring.TimeComponent(candidate, Request(60), Snapshot(candidate)), 10);
        Assert.Equal(1, RecommendationScoring.TimeComponent(candidate, Request(), Snapshot(candidate)));
    }

    [Fact]
    public void DiscoveryBoostIsFlatAndAdditive()
    {
        var snapshot = Snapshot(Candidate(1), Candidate(2) with { NeverPlayed = true });
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        Assert.Equal(0, scores[0].DiscoveryBoost);
        Assert.Equal(0.15, scores[1].DiscoveryBoost);
        Assert.Equal(0.15, scores[1].TotalScore - scores[0].TotalScore, 10);
    }

    [Fact]
    public void WeightedSamplingIsTopHeavyButSelectsEveryCandidate()
    {
        var snapshot = Snapshot(Active(1, 100), Candidate(2), Active(3, 10));
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        var random = new Random(1024);
        var counts = new int[3];
        for (var trial = 0; trial < 1000; trial++)
            counts[WeightedSelection.Select(scores, random).Candidate.Game.GameId - 1]++;

        Assert.InRange(counts[0], 501, 999);
        Assert.All(counts, count => Assert.True(count > 0));
        var weights = scores.Select(score => Math.Exp(score.TotalScore / 0.18)).ToArray();
        for (var index = 0; index < counts.Length; index++)
            Assert.InRange(counts[index] / 1000.0, weights[index] / weights.Sum() - 0.05, weights[index] / weights.Sum() + 0.05);
    }

    [Fact]
    public void SameSeedAndInputsProduceTheSameSelections()
    {
        var snapshot = Snapshot(Candidate(1), Candidate(2), Candidate(3));
        var scores = RecommendationScoring.Score(snapshot.Candidates, Request(), snapshot);
        var first = new Random(5);
        var second = new Random(5);
        for (var trial = 0; trial < 30; trial++)
            Assert.Equal(WeightedSelection.Select(scores, first), WeightedSelection.Select(scores, second));
    }
}

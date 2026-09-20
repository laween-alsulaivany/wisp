using Wisp.Core.Entities;
using Wisp.Core.Enums;

namespace Wisp.RecommendationEngine;

internal sealed record ScoredCandidate(
    RecommendationCandidate Candidate, double DecayedRank, double RankComponent,
    double TimeComponent, double DiscoveryBoost, double TotalScore);

internal static class RecommendationScoring
{
    internal static double? ExpectedMinutes(
        RecommendationCandidate candidate, RecommendationInputSnapshot snapshot) =>
        (candidate.LoggedSessionCount >= RecommendationConstants.MinimumPersonalSessionCount
            ? candidate.PersonalMedianSessionMinutes : null)
        ?? snapshot.ProfileWideMedianSessionMinutes
        ?? candidate.CompletionEstimateMainStoryMinutes;

    internal static double DecayedRank(RecommendationCandidate candidate, DateTimeOffset now)
    {
        if (candidate.State != GameStateKind.Active)
            return 0;

        var daysSince = candidate.StateChangedUtc is { } changed ? (now - changed).TotalDays : 0;
        return candidate.StoredActiveRankScore *
            Math.Exp(-Math.Log(2) / RecommendationConstants.HalfLifeDays * daysSince);
    }

    internal static double TimeComponent(RecommendationCandidate candidate,
        RecommendationRequest request, RecommendationInputSnapshot snapshot)
    {
        var expected = ExpectedMinutes(candidate, snapshot);
        if (request.Time.TargetDuration is not { } target || expected is null)
            return 1;

        return Math.Clamp(1 - Math.Abs(expected.Value - target.TotalMinutes) /
            RecommendationConstants.ToleranceWindowMinutes, RecommendationConstants.MinimumTimeComponent, 1);
    }

    internal static ScoredCandidate[] Score(IReadOnlyList<RecommendationCandidate> pool,
        RecommendationRequest request, RecommendationInputSnapshot snapshot)
    {
        var ranks = pool.Select(candidate => DecayedRank(candidate, snapshot.CurrentTimeUtc)).ToArray();
        var activeRanks = pool.Select((candidate, index) => (candidate, index))
            .Where(item => item.candidate.State == GameStateKind.Active)
            .Select(item => ranks[item.index]).ToArray();
        var min = activeRanks.Length == 0 ? 0 : activeRanks.Min();
        var max = activeRanks.Length == 0 ? 0 : activeRanks.Max();

        return pool.Select((candidate, index) =>
        {
            var rank = candidate.State != GameStateKind.Active
                ? RecommendationConstants.BaselineConstant
                : max == min ? 1 : (ranks[index] - min) / (max - min);
            var time = TimeComponent(candidate, request, snapshot);
            var discovery = candidate.NeverPlayed ? RecommendationConstants.DiscoveryBoost : 0;
            return new ScoredCandidate(candidate, ranks[index], rank, time, discovery,
                RecommendationConstants.RankWeight * rank + RecommendationConstants.TimeWeight * time + discovery);
        }).ToArray();
    }
}

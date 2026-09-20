using Wisp.Core.Entities;
using Wisp.Core.Enums;

namespace Wisp.RecommendationEngine;

internal sealed record GameRecommendationContext(
    ScoredCandidate Score, double DecayedRankPercentile, double StoredRankPercentile,
    double? DaysSinceLastSession, int PoolSize)
{
    internal RecommendationCandidate Candidate => Score.Candidate;
}

internal sealed record ReasonRule(
    Func<GameRecommendationContext, bool> Matches, Func<GameRecommendationContext, string> Message);

internal static class RecommendationReasons
{
    internal static readonly ReasonRule KeepGoingStreakRule = new(
        context => context.Candidate.ConsecutiveKeepGoingStreak >= RecommendationConstants.MinimumKeepGoingStreak,
        context => $"You marked this 'Keep going' after your last {context.Candidate.ConsecutiveKeepGoingStreak} sessions.");

    internal static readonly ReasonRule TopRankedActiveRule = new(
        context => context.Candidate.State == GameStateKind.Active &&
            context.DecayedRankPercentile >= RecommendationConstants.TopActivePercentile,
        _ => "This is currently one of your highest-ranked active games.");

    internal static readonly ReasonRule LongAbsenceHighRankRule = new(
        context => context.Candidate.State == GameStateKind.Active &&
            context.DaysSinceLastSession >= RecommendationConstants.LongAbsenceDays &&
            context.StoredRankPercentile >= RecommendationConstants.TopQuartilePercentile,
        context => $"You haven't returned to this in {(int)(context.DaysSinceLastSession!.Value / RecommendationConstants.DaysPerMonth)} months despite strong previous feedback.");

    internal static readonly ReasonRule DiscoveryRule = new(
        context => context.Candidate.NeverPlayed && context.PoolSize < RecommendationConstants.ThinPoolSize,
        _ => "You haven't tried this one yet.");

    // Feedback streaks and long absences are more specific than rank or discovery alone.
    private static readonly ReasonRule[] Rules =
        [KeepGoingStreakRule, LongAbsenceHighRankRule, TopRankedActiveRule, DiscoveryRule];

    internal static GameRecommendationContext Context(ScoredCandidate selected,
        IReadOnlyList<ScoredCandidate> pool, DateTimeOffset now)
    {
        var active = pool.Where(item => item.Candidate.State == GameStateKind.Active).ToArray();
        // Empirical percentiles give tied ranks the same percentile, including a singleton at 100.
        var decayedPercentile = active.Length == 0 ? 0 :
            100.0 * active.Count(item => item.DecayedRank <= selected.DecayedRank) / active.Length;
        var storedPercentile = active.Length == 0 ? 0 :
            100.0 * active.Count(item => item.Candidate.StoredActiveRankScore <= selected.Candidate.StoredActiveRankScore) / active.Length;
        return new GameRecommendationContext(selected, decayedPercentile, storedPercentile,
            selected.Candidate.LastSessionUtc is { } last ? (now - last).TotalDays : null, pool.Count);
    }

    internal static string[] Generate(GameRecommendationContext context) =>
        Rules.Where(rule => rule.Matches(context)).Take(RecommendationConstants.MaximumReasons)
            .Select(rule => rule.Message(context)).ToArray();
}

using Wisp.Core.Entities;
using Wisp.Core.Enums;

namespace Wisp.RecommendationEngine;

internal static class RecommendationPool
{
    private static readonly string[] LowEnergyTags =
        ["Relaxing", "Casual", "Walking Simulator", "Puzzle", "Cozy", "Atmospheric", "Point & Click"];
    private static readonly string[] NormalTags =
        ["Adventure", "RPG", "Simulation", "Strategy", "Story Rich", "Open World"];
    private static readonly string[] HighEnergyTags =
        ["Action", "FPS", "Fast-Paced", "Roguelike", "Souls-like", "Competitive", "Hack and Slash"];

    internal static RecommendationCandidate[] ForTier(
        RecommendationRequest request, RecommendationInputSnapshot snapshot, int tier)
    {
        return snapshot.Candidates.Where(candidate =>
            (tier >= 4 || MatchesMood(candidate, request.Mood)) &&
            (tier >= 3 || MatchesTime(candidate, request, snapshot, tier))).ToArray();
    }

    internal static bool MatchesMood(RecommendationCandidate candidate, MoodFilter mood)
    {
        if (mood == MoodFilter.Anything)
            return true;

        var tags = mood switch
        {
            MoodFilter.LowEnergy => LowEnergyTags,
            MoodFilter.Normal => NormalTags,
            MoodFilter.HighEnergy => HighEnergyTags,
            _ => throw new ArgumentOutOfRangeException(nameof(mood))
        };
        return candidate.Game.Tags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
    }

    private static bool MatchesTime(RecommendationCandidate candidate, RecommendationRequest request,
        RecommendationInputSnapshot snapshot, int tier)
    {
        var expected = RecommendationScoring.ExpectedMinutes(candidate, snapshot);
        // Missing duration metadata is neutral, just as it is in scoring.
        if (request.Time.TargetDuration is not { } target || expected is null)
            return true;

        var tolerance = tier == 1
            ? RecommendationConstants.TightToleranceMinutes
            : RecommendationConstants.LooseToleranceMinutes;
        return Math.Abs(expected.Value - target.TotalMinutes) <= tolerance;
    }
}

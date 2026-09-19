using Wisp.Core.Entities;

namespace Wisp.Core.Dtos;

/// <summary>
/// Sections 5.2 and 5.5 require the game/state, score components, feedback streak,
/// rank percentiles (0-100), optional days since the last session, logged session
/// count, and eligible pool size to evaluate the four documented reason rules.
/// </summary>
public sealed record GameRecommendationContext
{
    public required Game Game { get; init; }
    public required GameState State { get; init; }
    public double RankComponent { get; init; }
    public double TimeComponent { get; init; }
    public double DiscoveryBoost { get; init; }
    public int ConsecutiveKeepGoingCount { get; init; }
    public double DecayedRankPercentile { get; init; }
    public double StoredRankPercentile { get; init; }
    public double? DaysSinceLastSession { get; init; }
    public int LoggedSessionCount { get; init; }
    public int EligiblePoolSize { get; init; }
}

using Wisp.Core.Enums;

namespace Wisp.Core.Entities;

public sealed record RecommendationCandidate
{
    public required Game Game { get; init; }
    public GameStateKind State { get; init; } = GameStateKind.NoData;
    public double StoredActiveRankScore { get; init; }
    public DateTimeOffset? StateChangedUtc { get; init; }
    public DateTimeOffset? MaybeLaterUntilUtc { get; init; }
    public double? PersonalMedianSessionMinutes { get; init; }
    public int LoggedSessionCount { get; init; }
    public int? CompletionEstimateMainStoryMinutes { get; init; }
    public int ConsecutiveKeepGoingStreak { get; init; }
    public DateTimeOffset? LastSessionUtc { get; init; }
    public bool NeverPlayed { get; init; }
}

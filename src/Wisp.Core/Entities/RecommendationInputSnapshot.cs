namespace Wisp.Core.Entities;

public sealed record RecommendationInputSnapshot
{
    public required DateTimeOffset CurrentTimeUtc { get; init; }
    public double? ProfileWideMedianSessionMinutes { get; init; }
    public required IReadOnlyList<RecommendationCandidate> Candidates { get; init; }
}

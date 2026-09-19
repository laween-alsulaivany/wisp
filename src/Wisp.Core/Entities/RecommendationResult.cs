namespace Wisp.Core.Entities;

public sealed record RecommendationResult
{
    public required Game Game { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public double Score { get; init; }
    public int FallbackTierUsed { get; init; }
}

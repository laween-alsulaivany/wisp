using Wisp.Core.Dtos;
using Wisp.Core.Enums;

namespace Wisp.Core.Entities;

public sealed record RecommendationRequest
{
    public required int ProfileId { get; init; }
    public MoodFilter Mood { get; init; } = MoodFilter.Anything;
    public TimeFilter Time { get; init; }
    public AdvancedFilters Advanced { get; init; } = AdvancedFilters.None;
    public IReadOnlySet<int> ExcludedGameIdsThisSession { get; init; } = new HashSet<int>();
}

using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface IRecommendationSnapshotProvider
{
    Task<RecommendationInputSnapshot> BuildSnapshotAsync(int profileId, CancellationToken ct);
}

using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface IRecommendationEngine
{
    Task<RecommendationResult?> GetRecommendationAsync(RecommendationRequest request, CancellationToken ct);
}

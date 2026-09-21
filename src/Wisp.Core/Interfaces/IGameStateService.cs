using Wisp.Core.Entities;
using Wisp.Core.Enums;

namespace Wisp.Core.Interfaces;

public interface IGameStateService
{
    Task ApplyFeedbackAsync(int sessionId, FeedbackType feedback, CancellationToken ct);
    Task RestoreAsync(int gameId, int profileId, GameStateKind newState, CancellationToken ct);
    Task ResetRecommendationHistoryAsync(int profileId, CancellationToken ct);
}

using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface IGameStateRepository
{
    Task<GameState?> GetAsync(int gameId, int profileId, CancellationToken ct);
    Task UpsertAsync(GameState state, CancellationToken ct);
}

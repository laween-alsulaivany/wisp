using Wisp.Core.Dtos;
using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface IGameRepository
{
    Task UpsertAsync(Game game, CancellationToken ct);
    Task<Game?> GetByAppIdAsync(long appId, CancellationToken ct);
    Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<Game>> GetEligiblePoolAsync(int profileId, EligibilityFilter filter, CancellationToken ct);
}

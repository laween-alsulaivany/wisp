using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface ISteamProfileRepository
{
    Task<int> UpsertAsync(SteamProfile profile, CancellationToken ct);
    Task<SteamProfile?> GetByIdAsync(int profileId, CancellationToken ct);
    Task<SteamProfile?> GetBySteamId64Async(string steamId64, CancellationToken ct);
    Task<IReadOnlyList<SteamProfile>> GetAllAsync(CancellationToken ct);
    Task DeleteAsync(int profileId, CancellationToken ct);
}

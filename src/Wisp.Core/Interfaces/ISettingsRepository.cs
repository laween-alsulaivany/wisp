using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface ISettingsRepository
{
    Task<ProfileSettings?> GetAsync(int profileId, CancellationToken ct);
    Task UpsertAsync(ProfileSettings settings, CancellationToken ct);
    Task DeleteAsync(int profileId, CancellationToken ct);
}
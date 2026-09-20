using Wisp.Core.Dtos;
using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface ISessionRepository
{
    Task<int> StartSessionAsync(Session session, CancellationToken ct);
    Task CompleteSessionAsync(int sessionId, DateTimeOffset endUtc, int runtimeSeconds, int activeForegroundSeconds, CancellationToken ct);
    Task RecordRestartAsync(int sessionId, DateTimeOffset closedUtc, DateTimeOffset relaunchedUtc, CancellationToken ct);
    Task<Session?> GetByIdAsync(int sessionId, CancellationToken ct);
    Task<PlaytimeDistribution> GetActivePlaytimeDistributionAsync(int profileId, long? appId, CancellationToken ct);
}

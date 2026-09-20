using Dapper;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.Data.Repositories;

public sealed class SessionRepository(WispDatabase database) : ISessionRepository
{
    public Task<int> StartSessionAsync(Session session, CancellationToken ct) => database.WriteAsync(connection =>
        connection.ExecuteScalarAsync<int>(WispDatabase.Command("""
            INSERT INTO Sessions (GameId, ProfileId, StartUtc, ActiveForegroundSeconds, RestartCount, LaunchSource)
            VALUES (@GameId, @ProfileId, @StartUtc, @ActiveForegroundSeconds, @RestartCount, @LaunchSource)
            RETURNING SessionId;
            """, session, ct)), ct);

    public Task CompleteSessionAsync(int sessionId, DateTimeOffset endUtc, int runtimeSeconds,
        int activeForegroundSeconds, CancellationToken ct) => database.WriteAsync(async connection =>
    {
        var count = await connection.ExecuteAsync(WispDatabase.Command("""
            UPDATE Sessions SET EndUtc = @EndUtc, RuntimeSeconds = @RuntimeSeconds,
                ActiveForegroundSeconds = @ActiveForegroundSeconds WHERE SessionId = @SessionId;
            """, new { SessionId = sessionId, EndUtc = endUtc, RuntimeSeconds = runtimeSeconds,
                ActiveForegroundSeconds = activeForegroundSeconds }, ct));
        if (count == 0)
            throw new KeyNotFoundException($"Session {sessionId} does not exist.");
        return count;
    }, ct);

    public Task RecordRestartAsync(int sessionId, DateTimeOffset closedUtc, DateTimeOffset relaunchedUtc,
        CancellationToken ct) => database.WriteAsync(async connection =>
    {
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(WispDatabase.Command("""
            INSERT INTO SessionRestartEvents (SessionId, ClosedUtc, RelaunchedUtc)
            VALUES (@SessionId, @ClosedUtc, @RelaunchedUtc);
            UPDATE Sessions SET RestartCount = RestartCount + 1 WHERE SessionId = @SessionId;
            """, new { SessionId = sessionId, ClosedUtc = closedUtc, RelaunchedUtc = relaunchedUtc }, ct, transaction));
        transaction.Commit();
        return 0;
    }, ct);

    public async Task<Session?> GetByIdAsync(int sessionId, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Session>(WispDatabase.Command(
            "SELECT * FROM Sessions WHERE SessionId = @SessionId;", new { SessionId = sessionId }, ct));
    }

    public async Task<PlaytimeDistribution> GetActivePlaytimeDistributionAsync(int profileId, long? appId,
        CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        var samples = await connection.QueryAsync<int>(WispDatabase.Command("""
            SELECT s.ActiveForegroundSeconds FROM Sessions s
            JOIN Games g ON g.GameId = s.GameId
            WHERE s.ProfileId = @ProfileId AND (@AppId IS NULL OR g.AppId = @AppId) AND s.EndUtc IS NOT NULL
            ORDER BY s.SessionId;
            """, new { ProfileId = profileId, AppId = appId }, ct));
        return new PlaytimeDistribution { ActiveForegroundSeconds = samples.ToArray() };
    }
}

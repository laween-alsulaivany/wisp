using Dapper;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.Data.Repositories;

public sealed class SteamProfileRepository(WispDatabase database) : ISteamProfileRepository
{
    public Task<int> UpsertAsync(SteamProfile profile, CancellationToken ct) => database.WriteAsync(connection =>
        connection.ExecuteScalarAsync<int>(WispDatabase.Command("""
            INSERT INTO SteamProfiles (SteamId64, AccountName, PersonaName, LastSeenUtc)
            VALUES (@SteamId64, @AccountName, @PersonaName, @LastSeenUtc)
            ON CONFLICT(SteamId64) DO UPDATE SET AccountName = excluded.AccountName,
                PersonaName = excluded.PersonaName, LastSeenUtc = excluded.LastSeenUtc
            RETURNING ProfileId;
            """, profile, ct)), ct);

    public async Task<SteamProfile?> GetByIdAsync(int profileId, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<SteamProfile>(WispDatabase.Command(
            "SELECT * FROM SteamProfiles WHERE ProfileId = @ProfileId;", new { ProfileId = profileId }, ct));
    }

    public async Task<SteamProfile?> GetBySteamId64Async(string steamId64, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<SteamProfile>(WispDatabase.Command(
            "SELECT * FROM SteamProfiles WHERE SteamId64 = @SteamId64;", new { SteamId64 = steamId64 }, ct));
    }

    public async Task<IReadOnlyList<SteamProfile>> GetAllAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return (await connection.QueryAsync<SteamProfile>(WispDatabase.Command(
            "SELECT * FROM SteamProfiles ORDER BY ProfileId;", null, ct))).ToArray();
    }

    public Task DeleteAsync(int profileId, CancellationToken ct) => database.WriteAsync(connection =>
        connection.ExecuteAsync(WispDatabase.Command("DELETE FROM SteamProfiles WHERE ProfileId = @ProfileId;",
            new { ProfileId = profileId }, ct)), ct);
}

using Dapper;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.Data.Repositories;

public sealed class GameStateRepository(WispDatabase database) : IGameStateRepository
{
    public async Task<GameState?> GetAsync(int gameId, int profileId, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<GameState>(WispDatabase.Command(
            "SELECT * FROM GameStates WHERE GameId = @GameId AND ProfileId = @ProfileId;",
            new { GameId = gameId, ProfileId = profileId }, ct));
    }

    public Task UpsertAsync(GameState state, CancellationToken ct) => database.WriteAsync(connection =>
        connection.ExecuteAsync(WispDatabase.Command("""
            INSERT INTO GameStates (GameId, ProfileId, State, ActiveRankScore, MaybeLaterUntilUtc, StateChangedUtc)
            VALUES (@GameId, @ProfileId, @State, @ActiveRankScore, @MaybeLaterUntilUtc, @StateChangedUtc)
            ON CONFLICT(GameId, ProfileId) DO UPDATE SET State = excluded.State,
                ActiveRankScore = excluded.ActiveRankScore, MaybeLaterUntilUtc = excluded.MaybeLaterUntilUtc,
                StateChangedUtc = excluded.StateChangedUtc;
            """, state, ct)), ct);
}

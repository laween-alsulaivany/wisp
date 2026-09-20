using Dapper;
using Microsoft.Data.Sqlite;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.Data.Repositories;

public sealed class GameRepository(WispDatabase database) : IGameRepository
{
    public Task UpsertAsync(Game game, CancellationToken ct) => database.WriteAsync(async connection =>
    {
        using var transaction = connection.BeginTransaction();
        var gameId = await connection.ExecuteScalarAsync<int>(WispDatabase.Command("""
            INSERT INTO Games (AppId, Name, Installed, InstallDir, IsFreeToPlay, IsToolOrUtility, IsDemo, IsVrOnly,
                SupportsController, IsSinglePlayer, IsMultiplayer, IsStoryFocused, HeaderImagePath,
                HeaderImageFetchedUtc, MetadataFetchedUtc, MetadataStale,
                SteamCumulativePlaytimeMinutes, SteamLastPlayedUtc, CreatedUtc, UpdatedUtc)
            VALUES (@AppId, @Name, @Installed, @InstallDir, @IsFreeToPlay, @IsToolOrUtility, @IsDemo, @IsVrOnly,
                @SupportsController, @IsSinglePlayer, @IsMultiplayer, @IsStoryFocused, @HeaderImagePath,
                @HeaderImageFetchedUtc, @MetadataFetchedUtc, @MetadataStale,
                @SteamCumulativePlaytimeMinutes, @SteamLastPlayedUtc, @Now, @Now)
            ON CONFLICT(AppId) DO UPDATE SET
                Name = excluded.Name, Installed = excluded.Installed, InstallDir = excluded.InstallDir,
                IsFreeToPlay = excluded.IsFreeToPlay,
                IsToolOrUtility = excluded.IsToolOrUtility, IsDemo = excluded.IsDemo, IsVrOnly = excluded.IsVrOnly,
                SupportsController = excluded.SupportsController,
                IsSinglePlayer = excluded.IsSinglePlayer, IsMultiplayer = excluded.IsMultiplayer,
                IsStoryFocused = excluded.IsStoryFocused, HeaderImagePath = excluded.HeaderImagePath,
                HeaderImageFetchedUtc = excluded.HeaderImageFetchedUtc,
                MetadataFetchedUtc = excluded.MetadataFetchedUtc, MetadataStale = excluded.MetadataStale,
                SteamCumulativePlaytimeMinutes = excluded.SteamCumulativePlaytimeMinutes,
                SteamLastPlayedUtc = excluded.SteamLastPlayedUtc, UpdatedUtc = excluded.UpdatedUtc
            RETURNING GameId;
            """, new
        {
            game.AppId, game.Name, game.Installed, game.InstallDir, game.IsFreeToPlay, game.IsToolOrUtility, game.IsDemo,
            game.IsVrOnly, game.SupportsController, game.IsSinglePlayer, game.IsMultiplayer, game.IsStoryFocused,
            game.HeaderImagePath, game.HeaderImageFetchedUtc, game.MetadataFetchedUtc, game.MetadataStale,
            game.SteamCumulativePlaytimeMinutes,
            game.SteamLastPlayedUtc, Now = DateTimeOffset.UtcNow
        }, ct, transaction));

        await connection.ExecuteAsync(WispDatabase.Command("DELETE FROM GameTags WHERE GameId = @GameId;",
            new { GameId = gameId }, ct, transaction));
        foreach (var tag in game.Tags.Distinct(StringComparer.Ordinal))
        {
            var tagId = await connection.QuerySingleOrDefaultAsync<int?>(WispDatabase.Command(
                "SELECT TagId FROM TagDictionary WHERE TagName = @Tag;", new { Tag = tag }, ct, transaction));
            if (tagId is null)
                throw new ArgumentException($"Tag '{tag}' is not in the bundled tag dictionary.", nameof(game));
            await connection.ExecuteAsync(WispDatabase.Command(
                "INSERT INTO GameTags (GameId, TagId) VALUES (@GameId, @TagId);",
                new { GameId = gameId, TagId = tagId.Value }, ct, transaction));
        }

        transaction.Commit();
        return gameId;
    }, ct);

    public async Task<Game?> GetByAppIdAsync(long appId, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        var games = await ReadGamesAsync(connection, "SELECT g.* FROM Games g WHERE g.AppId = @AppId;",
            new { AppId = appId }, ct);
        return games.SingleOrDefault();
    }

    public async Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return await ReadGamesAsync(connection, "SELECT g.* FROM Games g ORDER BY g.GameId;", null, ct);
    }

    public async Task<IReadOnlyList<Game>> GetEligiblePoolAsync(int profileId, EligibilityFilter filter, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return await ReadGamesAsync(connection, """
            SELECT g.* FROM Games g
            LEFT JOIN GameStates s ON s.GameId = g.GameId AND s.ProfileId = @ProfileId
            WHERE g.Installed = 1 AND g.IsToolOrUtility = 0
                AND (g.IsDemo = 0 OR @IncludeDemos = 1)
                AND (g.IsVrOnly = 0 OR @IncludeVrOnly = 1)
                AND (s.State IS NULL OR s.State <> 'Dropped')
                AND (s.State IS NULL OR s.State <> 'Finished' OR @IncludeFinishedGames = 1)
                AND (s.State IS NULL OR s.State <> 'MaybeLater'
                    OR s.MaybeLaterUntilUtc IS NULL OR s.MaybeLaterUntilUtc <= @UtcNow)
            ORDER BY g.GameId;
            """, new { ProfileId = profileId, filter.IncludeFinishedGames, filter.IncludeDemos,
                filter.IncludeVrOnly, filter.UtcNow }, ct);
    }

    private static async Task<IReadOnlyList<Game>> ReadGamesAsync(SqliteConnection connection, string sql,
        object? parameters, CancellationToken ct)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var games = (await connection.QueryAsync<Game>(WispDatabase.Command(sql, parameters, ct, transaction))).ToArray();
        if (games.Length == 0)
            return games;

        var tags = (await connection.QueryAsync<GameTag>(WispDatabase.Command("""
            SELECT gt.GameId, td.TagName FROM GameTags gt
            JOIN TagDictionary td ON td.TagId = gt.TagId
            WHERE gt.GameId IN @Ids ORDER BY td.TagName;
            """, new { Ids = games.Select(game => game.GameId).ToArray() }, ct, transaction)))
            .ToLookup(tag => tag.GameId, tag => tag.TagName);
        return games.Select(game => game with { Tags = tags[game.GameId].ToArray() }).ToArray();
    }

    private sealed class GameTag
    {
        public int GameId { get; set; }
        public string TagName { get; set; } = string.Empty;
    }
}

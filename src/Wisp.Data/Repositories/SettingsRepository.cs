using Dapper;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.Data.Repositories;

public sealed class SettingsRepository(WispDatabase database) : ISettingsRepository
{
    public async Task<ProfileSettings?> GetAsync(int profileId, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<ProfileSettings>(WispDatabase.Command(
            "SELECT * FROM ProfileSettings WHERE ProfileId = @ProfileId;", new { ProfileId = profileId }, ct));
    }

    public Task UpsertAsync(ProfileSettings settings, CancellationToken ct) => database.WriteAsync(connection =>
        connection.ExecuteAsync(WispDatabase.Command("""
            INSERT INTO ProfileSettings (ProfileId, MaybeLaterCooldownDays, IncludeFinishedGames, IncludeDemos,
                IncludeVrOnly, StartWithWindows, ShowPostSessionFeedback, AdvancedFiltersExpanded)
            VALUES (@ProfileId, @MaybeLaterCooldownDays, @IncludeFinishedGames, @IncludeDemos,
                @IncludeVrOnly, @StartWithWindows, @ShowPostSessionFeedback, @AdvancedFiltersExpanded)
            ON CONFLICT(ProfileId) DO UPDATE SET MaybeLaterCooldownDays = excluded.MaybeLaterCooldownDays,
                IncludeFinishedGames = excluded.IncludeFinishedGames, IncludeDemos = excluded.IncludeDemos,
                IncludeVrOnly = excluded.IncludeVrOnly, StartWithWindows = excluded.StartWithWindows,
                ShowPostSessionFeedback = excluded.ShowPostSessionFeedback,
                AdvancedFiltersExpanded = excluded.AdvancedFiltersExpanded;
            """, settings, ct)), ct);

    public Task DeleteAsync(int profileId, CancellationToken ct) => database.WriteAsync(connection =>
        connection.ExecuteAsync(WispDatabase.Command("DELETE FROM ProfileSettings WHERE ProfileId = @ProfileId;",
            new { ProfileId = profileId }, ct)), ct);
}

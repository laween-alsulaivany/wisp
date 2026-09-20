using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Xunit;

namespace Wisp.Data.Tests;

public sealed class ProfileSettingsAndStateTests
{
    [Fact]
    public async Task ProfilesUpsertBySteamIdentityRoundTripAndDeleteOnlyTheirOwnData()
    {
        await using var db = await TestDatabase.CreateAsync();
        var original = new SteamProfile
        {
            SteamId64 = "76561198000000001", AccountName = "Account", PersonaName = "Persona",
            LastSeenUtc = TestDatabase.Now.ToOffset(TimeSpan.FromHours(-7))
        };
        var id = await db.Profiles.UpsertAsync(original, default);
        Assert.Equal(original with { ProfileId = id }, await db.Profiles.GetByIdAsync(id, default));
        var changed = original with { AccountName = "Changed", PersonaName = null, LastSeenUtc = TestDatabase.Now.AddDays(1) };
        Assert.Equal(id, await db.Profiles.UpsertAsync(changed, default));
        Assert.Equal(changed with { ProfileId = id }, await db.Profiles.GetBySteamId64Async(original.SteamId64, default));
        Assert.Single(await db.Profiles.GetAllAsync(default));
        Assert.Equal("2026-09-20T12:30:15.123Z", await db.ScalarAsync<string>("SELECT LastSeenUtc FROM SteamProfiles;"));
        Assert.Null(await db.Profiles.GetByIdAsync(999, default));
        Assert.Null(await db.Profiles.GetBySteamId64Async("missing", default));

        var other = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        var session = await db.AddSessionAsync(game.GameId, id);
        await db.Sessions.RecordRestartAsync(session, TestDatabase.Now, TestDatabase.Now.AddMinutes(1), default);
        await db.Feedback.RecordAsync(new Feedback
        {
            SessionId = session, GameId = game.GameId, ProfileId = id, IsPending = true, FeedbackType = FeedbackType.Pending
        }, default);
        await db.Settings.UpsertAsync(new ProfileSettings { ProfileId = id }, default);
        await db.States.UpsertAsync(new GameState
        {
            GameId = game.GameId, ProfileId = id, State = GameStateKind.Active, StateChangedUtc = TestDatabase.Now
        }, default);
        await db.AddSessionAsync(game.GameId, other);
        await db.Profiles.DeleteAsync(id, default);
        Assert.Null(await db.Profiles.GetByIdAsync(id, default));
        Assert.Equal(other, Assert.Single(await db.Profiles.GetAllAsync(default)).ProfileId);
        Assert.NotNull(await db.Games.GetByAppIdAsync(game.AppId, default));
        Assert.Null(await db.Settings.GetAsync(id, default));
        Assert.Null(await db.States.GetAsync(game.GameId, id, default));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Sessions;"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Feedback;"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM SessionRestartEvents;"));
    }

    [Fact]
    public async Task SettingsRoundTripDefaultsAndEveryChangedFieldAndDeleteIndependently()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProfileAsync();
        Assert.Null(await db.Settings.GetAsync(id, default));
        var defaults = new ProfileSettings { ProfileId = id };
        await db.Settings.UpsertAsync(defaults, default);
        Assert.Equal(defaults, await db.Settings.GetAsync(id, default));
        var updated = defaults with
        {
            MaybeLaterCooldownDays = 14, IncludeFinishedGames = true, IncludeDemos = true, IncludeVrOnly = true,
            StartWithWindows = false, ShowPostSessionFeedback = false, AdvancedFiltersExpanded = true
        };
        await db.Settings.UpsertAsync(updated, default);
        Assert.Equal(updated, await db.Settings.GetAsync(id, default));
        Assert.Equal("1:1:1:0:0:1", await db.ScalarAsync<string>("""
            SELECT IncludeFinishedGames || ':' || IncludeDemos || ':' || IncludeVrOnly || ':' ||
                StartWithWindows || ':' || ShowPostSessionFeedback || ':' || AdvancedFiltersExpanded FROM ProfileSettings;
            """));
        await db.Settings.DeleteAsync(id, default);
        Assert.Null(await db.Settings.GetAsync(id, default));
        Assert.NotNull(await db.Profiles.GetByIdAsync(id, default));
    }

    [Fact]
    public async Task GameStateRoundTripsAllFieldsAndUpdatesOnlyTheCompositeKey()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        Assert.Null(await db.States.GetAsync(game.GameId, id, default));
        var state = new GameState
        {
            GameId = game.GameId, ProfileId = id, State = GameStateKind.MaybeLater, ActiveRankScore = 42.5,
            ConsecutiveKeepGoingCount = 3,
            MaybeLaterUntilUtc = TestDatabase.Now.AddDays(7).ToOffset(TimeSpan.FromHours(5)), StateChangedUtc = TestDatabase.Now
        };
        await db.States.UpsertAsync(state, default);
        Assert.Equal(state, await db.States.GetAsync(game.GameId, id, default));
        await db.States.UpsertAsync(state with { ProfileId = other }, default);
        var updated = state with
        {
            State = GameStateKind.Active, ActiveRankScore = 56.25, MaybeLaterUntilUtc = null,
            ConsecutiveKeepGoingCount = 4
        };
        await db.States.UpsertAsync(updated, default);
        Assert.Equal(updated, await db.States.GetAsync(game.GameId, id, default));
        Assert.Equal(state with { ProfileId = other }, await db.States.GetAsync(game.GameId, other, default));
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM GameStates;"));
        await db.States.UpsertAsync(updated with { ConsecutiveKeepGoingCount = 0 }, default);
        Assert.Equal(0, (await db.States.GetAsync(game.GameId, id, default))!.ConsecutiveKeepGoingCount);
    }
}

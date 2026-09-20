using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Xunit;

namespace Wisp.Data.Tests;

public sealed class GameRepositoryTests
{
    [Fact]
    public async Task UpsertByAppIdRoundTripsEveryCoreFieldAndReplacesTagsWithoutReplacingIdentity()
    {
        await using var db = await TestDatabase.CreateAsync();
        var original = new Game
        {
            AppId = 4_000_000_000, Name = "Game's name", Installed = true, IsFreeToPlay = true,
            IsToolOrUtility = true, IsDemo = true, IsVrOnly = true, SupportsController = true,
            InstallDir = @"D:\SteamLibrary\steamapps\common\Game's name",
            IsSinglePlayer = true, IsMultiplayer = true, IsStoryFocused = true,
            HeaderImagePath = @"C:\Users\Player\AppData\Local\Wisp\artwork\4000000000.jpg",
            HeaderImageFetchedUtc = TestDatabase.Now.ToOffset(TimeSpan.FromHours(-6)),
            MetadataFetchedUtc = TestDatabase.Now.ToOffset(TimeSpan.FromHours(5.5)), MetadataStale = false,
            SteamCumulativePlaytimeMinutes = 5_000_000_000,
            SteamLastPlayedUtc = TestDatabase.Now.ToOffset(TimeSpan.FromHours(5.5)), Tags = ["Story", "Action"]
        };
        await db.Games.UpsertAsync(original, default);
        var first = (await db.Games.GetByAppIdAsync(original.AppId, default))!;
        Assert.True(first.GameId > 0);
        Assert.Equal(original with { GameId = first.GameId, Tags = first.Tags }, first);
        Assert.Equal(new[] { "Action", "Story" }, first.Tags);
        Assert.Equal(TimeSpan.Zero, first.SteamLastPlayedUtc!.Value.Offset);
        Assert.Equal("2026-09-19T12:30:15.123Z", await db.ScalarAsync<string>("SELECT SteamLastPlayedUtc FROM Games;"));
        Assert.Equal(TimeSpan.Zero, first.HeaderImageFetchedUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, first.MetadataFetchedUtc!.Value.Offset);
        Assert.Equal("2026-09-19T12:30:15.123Z", await db.ScalarAsync<string>("SELECT HeaderImageFetchedUtc FROM Games;"));
        Assert.Equal("2026-09-19T12:30:15.123Z", await db.ScalarAsync<string>("SELECT MetadataFetchedUtc FROM Games;"));
        foreach (var column in new[] { "IsSinglePlayer", "IsMultiplayer", "IsStoryFocused", "MetadataStale" })
        {
            Assert.Equal("integer", await db.ScalarAsync<string>($"SELECT typeof({column}) FROM Games;"));
            Assert.Equal(column == "MetadataStale" ? 0 : 1, await db.ScalarAsync<int>($"SELECT {column} FROM Games;"));
        }
        Assert.Equal("integer", await db.ScalarAsync<string>("SELECT typeof(Installed) FROM Games;"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT Installed FROM Games;"));
        var createdUtc = await db.ScalarAsync<string>("SELECT CreatedUtc FROM Games;");
        var profile = await db.AddProfileAsync();
        await db.AddSessionAsync(first.GameId, profile);

        var updated = original with
        {
            Name = "Renamed", Installed = false, IsFreeToPlay = false, IsToolOrUtility = false,
            IsDemo = false, IsVrOnly = false, SupportsController = false,
            InstallDir = null, IsSinglePlayer = false, IsMultiplayer = false, IsStoryFocused = false,
            HeaderImagePath = null, HeaderImageFetchedUtc = null, MetadataFetchedUtc = null, MetadataStale = true,
            SteamCumulativePlaytimeMinutes = 0, SteamLastPlayedUtc = null, Tags = ["Puzzle"]
        };
        await db.Games.UpsertAsync(updated, default);
        var second = (await db.Games.GetByAppIdAsync(original.AppId, default))!;
        Assert.Equal(updated with { GameId = first.GameId, Tags = second.Tags }, second);
        Assert.Equal(new[] { "Puzzle" }, second.Tags);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Games;"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Sessions;"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT Installed FROM Games;"));
        Assert.Equal(createdUtc, await db.ScalarAsync<string>("SELECT CreatedUtc FROM Games;"));
        Assert.Null(await db.Games.GetByAppIdAsync(999, default));
    }

    [Fact]
    public async Task NewGamePersistsStaleMetadataAndNullCacheFields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var game = await db.AddGameAsync();

        Assert.True(game.MetadataStale);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT MetadataStale FROM Games;"));
        Assert.Null(game.InstallDir);
        Assert.Null(game.HeaderImagePath);
        Assert.Null(game.HeaderImageFetchedUtc);
        Assert.Null(game.MetadataFetchedUtc);
    }

    [Fact]
    public async Task GetAllReturnsEmptyForAnEmptyLibrary()
    {
        await using var db = await TestDatabase.CreateAsync();

        Assert.Empty(await db.Games.GetAllAsync(default));
    }

    [Fact]
    public async Task GetAllReturnsEachGamesFieldsAndTagsWithoutRequiringAProfile()
    {
        await using var db = await TestDatabase.CreateAsync();
        var first = new Game
        {
            AppId = 420, Name = "First", Installed = true, InstallDir = @"D:\Steam\steamapps\common\First",
            IsSinglePlayer = true, IsMultiplayer = true, IsStoryFocused = true,
            MetadataStale = false, MetadataFetchedUtc = TestDatabase.Now,
            HeaderImagePath = @"C:\artwork\420.jpg", HeaderImageFetchedUtc = TestDatabase.Now,
            Tags = ["Story", "Action"]
        };
        var second = new Game { AppId = 10, Name = "Second", Tags = ["Puzzle"] };
        await db.Games.UpsertAsync(first, default);
        await db.Games.UpsertAsync(second, default);

        var games = await db.Games.GetAllAsync(default);

        Assert.Equal(new long[] { 420, 10 }, games.Select(game => game.AppId));
        Assert.Equal(first with { GameId = games[0].GameId, Tags = games[0].Tags }, games[0]);
        Assert.Equal(second with { GameId = games[1].GameId, Tags = games[1].Tags }, games[1]);
        Assert.Equal(new[] { "Action", "Story" }, games[0].Tags);
        Assert.Equal(new[] { "Puzzle" }, games[1].Tags);
    }

    [Fact]
    public async Task UnknownTagRollsBackEntireUpsertAndDoesNotInventSteamTagIds()
    {
        await using var db = await TestDatabase.CreateAsync();
        var game = await db.AddGameAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => db.Games.UpsertAsync(
            game with { Name = "Changed", Tags = ["Action", "Unknown"] }, default));
        Assert.Equal("Game", (await db.Games.GetByAppIdAsync(game.AppId, default))!.Name);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM GameTags;"));
        Assert.Equal(3, await db.ScalarAsync<int>("SELECT COUNT(*) FROM TagDictionary;"));
    }

    [Theory]
    [InlineData("Dropped")]
    [InlineData("Finished")]
    [InlineData("CoolingDown")]
    [InlineData("Tool")]
    [InlineData("DedicatedServer")]
    [InlineData("Editor")]
    [InlineData("Soundtrack")]
    [InlineData("Benchmark")]
    [InlineData("Demo")]
    [InlineData("Prologue")]
    [InlineData("VrOnly")]
    [InlineData("Uninstalled")]
    public async Task EligiblePoolExcludesEachReasonIndependently(string reason)
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var included = await db.AddGameAsync(1);
        var excluded = await db.AddGameAsync(2);
        excluded = excluded with
        {
            Name = reason, Installed = reason != "Uninstalled",
            IsToolOrUtility = reason is "Tool" or "DedicatedServer" or "Editor" or "Soundtrack" or "Benchmark",
            IsDemo = reason is "Demo" or "Prologue", IsVrOnly = reason == "VrOnly"
        };
        await db.Games.UpsertAsync(excluded, default);
        if (reason is "Dropped" or "Finished" or "CoolingDown")
            await db.States.UpsertAsync(new GameState
            {
                GameId = excluded.GameId, ProfileId = profile, StateChangedUtc = TestDatabase.Now,
                State = reason == "CoolingDown" ? GameStateKind.MaybeLater : Enum.Parse<GameStateKind>(reason),
                MaybeLaterUntilUtc = reason == "CoolingDown" ? TestDatabase.Now.AddDays(1) : null
            }, default);

        var pool = await db.Games.GetEligiblePoolAsync(profile, new EligibilityFilter { UtcNow = TestDatabase.Now }, default);
        Assert.Equal(included.AppId, Assert.Single(pool).AppId);
        Assert.Equal(new[] { included.AppId, excluded.AppId },
            (await db.Games.GetAllAsync(default)).Select(game => game.AppId));
    }

    [Theory]
    [InlineData("Finished")]
    [InlineData("Demo")]
    [InlineData("VrOnly")]
    public async Task CorrespondingFlagOverridesDefaultExclusion(string reason)
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var game = await db.AddGameAsync();
        await db.Games.UpsertAsync(game with { IsDemo = reason == "Demo", IsVrOnly = reason == "VrOnly" }, default);
        if (reason == "Finished")
            await db.States.UpsertAsync(new GameState
            {
                GameId = game.GameId, ProfileId = profile, State = GameStateKind.Finished, StateChangedUtc = TestDatabase.Now
            }, default);
        var filter = new EligibilityFilter
        {
            UtcNow = TestDatabase.Now, IncludeFinishedGames = reason == "Finished",
            IncludeDemos = reason == "Demo", IncludeVrOnly = reason == "VrOnly"
        };
        Assert.Equal(game.AppId, Assert.Single(await db.Games.GetEligiblePoolAsync(profile, filter, default)).AppId);
    }

    [Fact]
    public async Task DefaultsIncludeNeverPlayedFreeToPlayAndPlayedGamesAndIgnoreAnotherProfilesState()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        var neverPlayed = await db.AddGameAsync(1);
        var free = await db.AddGameAsync(2);
        await db.Games.UpsertAsync(free with { IsFreeToPlay = true }, default);
        var played = await db.AddGameAsync(3);
        await db.Games.UpsertAsync(played with { SteamCumulativePlaytimeMinutes = 90 }, default);
        await db.States.UpsertAsync(new GameState
        {
            GameId = neverPlayed.GameId, ProfileId = other, State = GameStateKind.Dropped, StateChangedUtc = TestDatabase.Now
        }, default);
        Assert.Equal(new long[] { 1, 2, 3 }, (await db.Games.GetEligiblePoolAsync(profile,
            new EligibilityFilter { UtcNow = TestDatabase.Now }, default)).Select(game => game.AppId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CooldownExpiresAtTheSuppliedUtcInstant(int secondsFromNow)
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var game = await db.AddGameAsync();
        await db.States.UpsertAsync(new GameState
        {
            GameId = game.GameId, ProfileId = profile, State = GameStateKind.MaybeLater,
            StateChangedUtc = TestDatabase.Now.AddDays(-7), MaybeLaterUntilUtc = TestDatabase.Now.AddSeconds(secondsFromNow)
        }, default);
        var pool = await db.Games.GetEligiblePoolAsync(profile,
            new EligibilityFilter { UtcNow = TestDatabase.Now.ToOffset(TimeSpan.FromHours(-6)) }, default);
        Assert.Equal(secondsFromNow <= 0 ? 1 : 0, pool.Count);
    }
}

using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.Data.Tests;

public sealed class RecommendationSnapshotProviderTests
{
    [Fact]
    public async Task EnrichesEligibleGamesWithProfileStateCompletedHistoryAndBundledEstimates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        await db.Games.UpsertAsync(game with { Tags = ["Puzzle", "Action"] }, default);
        var state = new GameState
        {
            GameId = game.GameId, ProfileId = profile, State = GameStateKind.Active,
            ActiveRankScore = 87.5, ConsecutiveKeepGoingCount = 4, StateChangedUtc = TestDatabase.Now.AddDays(-120)
        };
        await db.States.UpsertAsync(state, default);
        await db.States.UpsertAsync(state with
        {
            ProfileId = other, ActiveRankScore = 1, ConsecutiveKeepGoingCount = 99
        }, default);
        await AddCompletedAsync(db, game.GameId, profile, 1200, -4);
        await AddCompletedAsync(db, game.GameId, profile, 1800, -1);
        await AddCompletedAsync(db, game.GameId, profile, 36000, -3);
        await AddCompletedAsync(db, game.GameId, other, 60, 0);
        await db.AddSessionAsync(game.GameId, profile);
        var uninstalled = await db.AddGameAsync(421);
        await db.Games.UpsertAsync(uninstalled with { Installed = false }, default);
        await AddCompletedAsync(db, uninstalled.GameId, profile, 2400, -1);
        var clock = new CountingClock();
        IRecommendationSnapshotProvider provider = new RecommendationSnapshotProvider(db.Database, db.Games, db.Settings, clock);

        var snapshot = await provider.BuildSnapshotAsync(profile, default);

        Assert.Equal(1, clock.ReadCount);
        Assert.Equal(TestDatabase.Now, snapshot.CurrentTimeUtc);
        Assert.Equal(35, snapshot.ProfileWideMedianSessionMinutes);
        var candidate = Assert.Single(snapshot.Candidates);
        Assert.Equal(game.GameId, candidate.Game.GameId);
        Assert.Equal(new[] { "Action", "Puzzle" }, candidate.Game.Tags);
        Assert.Equal(GameStateKind.Active, candidate.State);
        Assert.Equal(87.5, candidate.StoredActiveRankScore);
        Assert.Equal(state.StateChangedUtc, candidate.StateChangedUtc);
        Assert.Null(candidate.MaybeLaterUntilUtc);
        Assert.Equal(4, candidate.ConsecutiveKeepGoingStreak);
        Assert.Equal(3, candidate.LoggedSessionCount);
        Assert.Equal(30, candidate.PersonalMedianSessionMinutes);
        Assert.Equal(TestDatabase.Now.AddDays(-1), candidate.LastSessionUtc);
        Assert.Equal(100, candidate.CompletionEstimateMainStoryMinutes);
        Assert.False(candidate.NeverPlayed);

        await db.States.UpsertAsync(state with { ConsecutiveKeepGoingCount = 5 }, default);
        Assert.Equal(4, candidate.ConsecutiveKeepGoingStreak);
        Assert.Equal(5, Assert.Single((await provider.BuildSnapshotAsync(profile, default)).Candidates).ConsecutiveKeepGoingStreak);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, 10.0)]
    [InlineData(2, 15.0)]
    [InlineData(3, 20.0)]
    [InlineData(4, 25.0)]
    public async Task UsesCompletedSessionMediansAndRequiresThreePersonalSamples(int count, double? median)
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var game = await db.AddGameAsync(999);
        int[] seconds = [600, 1200, 1800, 36000];
        foreach (var duration in seconds.Take(count))
            await AddCompletedAsync(db, game.GameId, profile, duration);

        var snapshot = await CreateProvider(db).BuildSnapshotAsync(profile, default);

        Assert.Equal(median, snapshot.ProfileWideMedianSessionMinutes);
        var candidate = Assert.Single(snapshot.Candidates);
        Assert.Equal(count >= 3 ? median : null, candidate.PersonalMedianSessionMinutes);
        Assert.Equal(count, candidate.LoggedSessionCount);
        Assert.Equal(count == 0, candidate.NeverPlayed);
        Assert.Equal(GameStateKind.NoData, candidate.State);
        Assert.Equal(0, candidate.StoredActiveRankScore);
        Assert.Equal(0, candidate.ConsecutiveKeepGoingStreak);
        Assert.Null(candidate.StateChangedUtc);
        Assert.Null(candidate.MaybeLaterUntilUtc);
        Assert.Null(candidate.CompletionEstimateMainStoryMinutes);
        Assert.Equal(count == 0 ? null : (DateTimeOffset?)TestDatabase.Now.AddDays(-1), candidate.LastSessionUtc);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0.0)]
    [InlineData(1, 2, 3, 36000, 2.5 / 60)]
    [InlineData(int.MaxValue - 3, int.MaxValue - 2, int.MaxValue - 1, int.MaxValue, (int.MaxValue - 1.5) / 60)]
    public async Task EvenMediansPreserveFractionalMinutesAndDoNotOverflow(int a, int b, int c, int d, double expected)
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var game = await db.AddGameAsync();
        foreach (var duration in new[] { d, b, a, c })
            await AddCompletedAsync(db, game.GameId, profile, duration);

        var snapshot = await CreateProvider(db).BuildSnapshotAsync(profile, default);

        Assert.Equal(expected, snapshot.ProfileWideMedianSessionMinutes);
        Assert.Equal(expected, Assert.Single(snapshot.Candidates).PersonalMedianSessionMinutes);
    }

    [Fact]
    public async Task SteamPlaytimePreventsDiscoveryButDoesNotInventLoggedSessions()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var game = await db.AddGameAsync();
        await db.Games.UpsertAsync(game with
        {
            SteamCumulativePlaytimeMinutes = 120, SteamLastPlayedUtc = TestDatabase.Now.AddDays(-10)
        }, default);

        var snapshot = await CreateProvider(db).BuildSnapshotAsync(profile, default);

        var candidate = Assert.Single(snapshot.Candidates);
        Assert.False(candidate.NeverPlayed);
        Assert.Equal(0, candidate.LoggedSessionCount);
        Assert.Null(candidate.LastSessionUtc);
        Assert.Null(candidate.PersonalMedianSessionMinutes);
        Assert.Null(snapshot.ProfileWideMedianSessionMinutes);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task UsesEachProfilesInclusionSettingsAndNeverRelaxesBaseExclusions(bool finished, bool demos, bool vr)
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        await db.Settings.UpsertAsync(new ProfileSettings
        {
            ProfileId = profile, IncludeFinishedGames = finished, IncludeDemos = demos, IncludeVrOnly = vr
        }, default);
        await db.Settings.UpsertAsync(new ProfileSettings
        {
            ProfileId = other, IncludeFinishedGames = !finished, IncludeDemos = !demos, IncludeVrOnly = !vr
        }, default);
        for (var appId = 1; appId <= 9; appId++)
        {
            var game = await db.AddGameAsync(appId);
            await db.Games.UpsertAsync(game with
            {
                IsDemo = appId == 2, IsVrOnly = appId == 3, IsToolOrUtility = appId == 6, Installed = appId != 7
            }, default);
            if (appId is 4 or 5 or 8 or 9)
                await db.States.UpsertAsync(new GameState
                {
                    GameId = game.GameId, ProfileId = profile, StateChangedUtc = TestDatabase.Now.AddDays(-7),
                    State = appId switch { 4 => GameStateKind.Finished, 5 => GameStateKind.Dropped, _ => GameStateKind.MaybeLater },
                    MaybeLaterUntilUtc = appId >= 8 ? TestDatabase.Now.AddSeconds(appId == 8 ? 1 : 0) : null
                }, default);
        }

        var snapshot = await CreateProvider(db).BuildSnapshotAsync(profile, default);

        var expected = new List<long> { 1 };
        if (demos) expected.Add(2);
        if (vr) expected.Add(3);
        if (finished) expected.Add(4);
        expected.Add(9);
        Assert.Equal(expected, snapshot.Candidates.Select(candidate => candidate.Game.AppId));
        var expired = snapshot.Candidates.Single(candidate => candidate.Game.AppId == 9);
        Assert.Equal(GameStateKind.MaybeLater, expired.State);
        Assert.Equal(TestDatabase.Now, expired.MaybeLaterUntilUtc);
    }

    [Fact]
    public async Task EmptyEligiblePoolStillHasProfileMedianFromIneligibleGames()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        await db.Games.UpsertAsync(game with { Installed = false }, default);
        await AddCompletedAsync(db, game.GameId, profile, 1800);

        var snapshot = await CreateProvider(db).BuildSnapshotAsync(profile, default);
        var otherSnapshot = await CreateProvider(db).BuildSnapshotAsync(other, default);

        Assert.Empty(snapshot.Candidates);
        Assert.Equal(30, snapshot.ProfileWideMedianSessionMinutes);
        Assert.Empty(otherSnapshot.Candidates);
        Assert.Null(otherSnapshot.ProfileWideMedianSessionMinutes);
    }

    [Fact]
    public async Task CancellationDoesNotReturnAPartialSnapshot()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateProvider(db).BuildSnapshotAsync(1, cancellation.Token));
    }

    private static RecommendationSnapshotProvider CreateProvider(TestDatabase db) =>
        new(db.Database, db.Games, db.Settings, new CountingClock());

    private static async Task AddCompletedAsync(TestDatabase db, int gameId, int profileId, int seconds, int days = -1)
    {
        var start = TestDatabase.Now.AddDays(days);
        var id = await db.Sessions.StartSessionAsync(new Session
        {
            GameId = gameId, ProfileId = profileId, StartUtc = start
        }, default);
        await db.Sessions.CompleteSessionAsync(id, start.AddSeconds(seconds), seconds, seconds, default);
    }

    private sealed class CountingClock : IClock
    {
        internal int ReadCount { get; private set; }
        public DateTimeOffset UtcNow { get { ReadCount++; return TestDatabase.Now; } }
    }
}

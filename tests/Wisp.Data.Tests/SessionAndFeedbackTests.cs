using Dapper;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Xunit;

namespace Wisp.Data.Tests;

public sealed class SessionAndFeedbackTests
{
    [Fact]
    public async Task GetByIdReturnsTheExactSessionBeforeAndAfterCompletion()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var otherProfile = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        var otherGame = await db.AddGameAsync(421);
        await db.AddSessionAsync(otherGame.GameId, otherProfile);
        var session = new Session
        {
            GameId = game.GameId, ProfileId = profile, StartUtc = TestDatabase.Now,
            LaunchSource = LaunchSource.Recommendation
        };
        var id = await db.Sessions.StartSessionAsync(session, default);
        session = session with { SessionId = id };
        Assert.Equal(session, await db.Sessions.GetByIdAsync(id, default));

        await db.Sessions.RecordRestartAsync(id, TestDatabase.Now.AddMinutes(3), TestDatabase.Now.AddMinutes(4), default);
        var endUtc = TestDatabase.Now.AddMinutes(30);
        await db.Sessions.CompleteSessionAsync(id, endUtc, 1740, 1500, default);
        Assert.Equal(session with
        {
            EndUtc = endUtc, RuntimeSeconds = 1740, ActiveForegroundSeconds = 1500, RestartCount = 1
        }, await db.Sessions.GetByIdAsync(id, default));
    }

    [Fact]
    public async Task GetByIdReturnsNullForMissingSession()
    {
        await using var db = await TestDatabase.CreateAsync();
        Assert.Null(await db.Sessions.GetByIdAsync(999, default));
    }

    [Fact]
    public async Task StartRestartAndCompleteRoundTripRuntimeAndForegroundTime()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var game = await db.AddGameAsync();
        var session = new Session
        {
            GameId = game.GameId, ProfileId = profile, StartUtc = TestDatabase.Now.ToOffset(TimeSpan.FromHours(3)),
            LaunchSource = LaunchSource.Manual
        };
        var id = await db.Sessions.StartSessionAsync(session, default);
        await using (var connection = await db.Database.OpenReadAsync(default))
        {
            var started = await connection.QuerySingleAsync<Session>("SELECT * FROM Sessions;");
            Assert.Equal(session with { SessionId = id }, started);
            Assert.Null(started.EndUtc);
            Assert.Null(started.RuntimeSeconds);
        }

        var closedUtc = TestDatabase.Now.AddMinutes(10);
        var relaunchedUtc = closedUtc.AddMinutes(2);
        await db.Sessions.RecordRestartAsync(id, closedUtc, relaunchedUtc, default);
        var endUtc = TestDatabase.Now.AddMinutes(45);
        var runtimeSeconds = (int)(endUtc - session.StartUtc).TotalSeconds;
        await db.Sessions.CompleteSessionAsync(id, endUtc.ToOffset(TimeSpan.FromHours(-5)), runtimeSeconds, 1800, default);

        await using var read = await db.Database.OpenReadAsync(default);
        var completed = await read.QuerySingleAsync<Session>("SELECT * FROM Sessions;");
        Assert.Equal(2700, completed.RuntimeSeconds);
        Assert.Equal(1800, completed.ActiveForegroundSeconds);
        Assert.Equal(endUtc, completed.EndUtc);
        Assert.Equal(1, completed.RestartCount);
        Assert.Equal(LaunchSource.Manual, completed.LaunchSource);
        Assert.Equal(TimeSpan.Zero, completed.StartUtc.Offset);
        Assert.Equal("2026-09-19T13:15:15.123Z", await read.ExecuteScalarAsync<string>("SELECT EndUtc FROM Sessions;"));
        Assert.Equal("2026-09-19T12:40:15.123Z", await read.ExecuteScalarAsync<string>("SELECT ClosedUtc FROM SessionRestartEvents;"));
        Assert.Equal("2026-09-19T12:42:15.123Z", await read.ExecuteScalarAsync<string>("SELECT RelaunchedUtc FROM SessionRestartEvents;"));
        Assert.Equal(id, await read.ExecuteScalarAsync<int>("SELECT SessionId FROM SessionRestartEvents;"));
    }

    [Fact]
    public async Task SamplesPreserveOutlierButTheirMedianIsNotSkewedLikeTheMean()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var otherProfile = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync(420);
        var otherGame = await db.AddGameAsync(421);
        foreach (var active in new[] { 1200, 1800, 2400, 3000, 100000 })
        {
            var id = await db.AddSessionAsync(game.GameId, profile);
            await db.Sessions.CompleteSessionAsync(id, TestDatabase.Now.AddSeconds(active + 600), active + 600, active, default);
        }
        await db.AddSessionAsync(game.GameId, profile); // Open sessions are not completed samples.
        var other = await db.AddSessionAsync(otherGame.GameId, profile);
        await db.Sessions.CompleteSessionAsync(other, TestDatabase.Now.AddHours(1), 3600, 600, default);
        var foreign = await db.AddSessionAsync(game.GameId, otherProfile);
        await db.Sessions.CompleteSessionAsync(foreign, TestDatabase.Now.AddHours(2), 7200, 7000, default);

        var samples = (await db.Sessions.GetActivePlaytimeDistributionAsync(profile, game.AppId, default)).ActiveForegroundSeconds;
        Assert.Equal(new[] { 1200, 1800, 2400, 3000, 100000 }, samples);
        var sorted = samples.Order().ToArray();
        var median = sorted[sorted.Length / 2];
        Assert.Equal(2400, median);
        Assert.True(samples.Average() > median * 5);
        Assert.Equal(6, (await db.Sessions.GetActivePlaytimeDistributionAsync(profile, null, default)).ActiveForegroundSeconds.Count);
        Assert.Equal(new[] { 7000 }, (await db.Sessions.GetActivePlaytimeDistributionAsync(otherProfile, null, default)).ActiveForegroundSeconds);
        Assert.Empty((await db.Sessions.GetActivePlaytimeDistributionAsync(profile, 999, default)).ActiveForegroundSeconds);
    }

    [Fact]
    public async Task PendingFeedbackUsesTheFlagAndIsScopedToProfileAndCanBeUpdated()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        var first = new Feedback
        {
            SessionId = await db.AddSessionAsync(game.GameId, profile), GameId = game.GameId, ProfileId = profile,
            FeedbackType = FeedbackType.Pending, IsPending = true
        };
        await db.Feedback.RecordAsync(first, default);
        await db.Feedback.RecordAsync(first with
        {
            SessionId = await db.AddSessionAsync(game.GameId, profile), IsPending = false,
            RecordedUtc = TestDatabase.Now.ToOffset(TimeSpan.FromHours(4))
        }, default);
        await db.Feedback.RecordAsync(first with
        {
            SessionId = await db.AddSessionAsync(game.GameId, other), ProfileId = other
        }, default);

        var pending = Assert.Single(await db.Feedback.GetPendingAsync(profile, default));
        Assert.Equal(first with { FeedbackId = pending.FeedbackId }, pending);
        Assert.Null(pending.RecordedUtc);
        await db.Feedback.RecordAsync(first with
        {
            FeedbackType = FeedbackType.KeepGoing, IsPending = false, Edited = true, RecordedUtc = TestDatabase.Now
        }, default);
        Assert.Empty(await db.Feedback.GetPendingAsync(profile, default));
        Assert.Single(await db.Feedback.GetPendingAsync(other, default));
        await using var read = await db.Database.OpenReadAsync(default);
        var edited = await read.QuerySingleAsync<Feedback>("SELECT * FROM Feedback WHERE SessionId = @Id;", new { Id = first.SessionId });
        Assert.Equal(pending.FeedbackId, edited.FeedbackId);
        Assert.Equal(FeedbackType.KeepGoing, edited.FeedbackType);
        Assert.Equal(TestDatabase.Now, edited.RecordedUtc);
        Assert.True(edited.Edited);
        Assert.Equal("integer:0:1", await read.ExecuteScalarAsync<string>(
            "SELECT typeof(IsPending) || ':' || IsPending || ':' || Edited FROM Feedback WHERE SessionId = @Id;",
            new { Id = first.SessionId }));
        Assert.Equal(3, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Feedback;"));
    }
}

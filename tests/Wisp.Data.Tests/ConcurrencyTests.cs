using Dapper;
using Microsoft.Data.Sqlite;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Data.Repositories;
using Xunit;

namespace Wisp.Data.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task SixtyConcurrentMixedWritesAcrossDatabaseInstancesAllSucceed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var game = await db.AddGameAsync();
        var sessions = new List<int>();
        for (var i = 0; i < 20; i++)
            sessions.Add(await db.AddSessionAsync(game.GameId, profile));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = Enumerable.Range(0, 60).Select(i => Task.Run(async () =>
        {
            var database = new WispDatabase(db.DatabasePath, db.EstimatesPath);
            await start.Task;
            switch (i % 3)
            {
                case 0:
                    await new GameRepository(database).UpsertAsync(new Game { AppId = 1000 + i, Name = $"Game {i}" }, default);
                    break;
                case 1:
                    await new SessionRepository(database).StartSessionAsync(new Session
                    {
                        GameId = game.GameId, ProfileId = profile, StartUtc = TestDatabase.Now
                    }, default);
                    break;
                default:
                    await new FeedbackRepository(database).RecordAsync(new Feedback
                    {
                        SessionId = sessions[i / 3], GameId = game.GameId, ProfileId = profile,
                        IsPending = true, FeedbackType = FeedbackType.Pending
                    }, default);
                    break;
            }
        })).ToArray();
        start.SetResult();

        await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(writes, task => Assert.True(task.IsCompletedSuccessfully));
        Assert.Equal(21, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Games;"));
        Assert.Equal(40, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Sessions;"));
        Assert.Equal(20, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Feedback;"));
        Assert.Equal("ok", await db.ScalarAsync<string>("PRAGMA integrity_check;"));
        await using var read = await db.Database.OpenReadAsync(default);
        Assert.Empty(await read.QueryAsync("PRAGMA foreign_key_check;"));
    }

    [Fact]
    public async Task ReadsDoNotWaitForTheWriteQueueAndCanceledWorkDoesNotWrite()
    {
        await using var db = await TestDatabase.CreateAsync();
        var game = await db.AddGameAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = db.Database.WriteAsync(async connection =>
        {
            using var transaction = connection.BeginTransaction();
            await connection.ExecuteAsync("UPDATE Games SET Name = 'Uncommitted';", transaction: transaction);
            entered.SetResult();
            await release.Task;
            transaction.Commit();
            return 0;
        }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var canceled = db.Games.UpsertAsync(game with { Name = "Canceled" }, cancellation.Token);
        try
        {
            cancellation.Cancel();
            var read = await db.Games.GetByAppIdAsync(game.AppId, default).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Game", read!.Name);
        }
        finally
        {
            release.SetResult();
            await writer;
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.Equal("Uncommitted", (await db.Games.GetByAppIdAsync(game.AppId, default))!.Name);
    }

    [Fact]
    public async Task FailedWriteDoesNotPoisonTheQueue()
    {
        await using var db = await TestDatabase.CreateAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Sessions.StartSessionAsync(new Session
        {
            GameId = 999, ProfileId = 999, StartUtc = TestDatabase.Now
        }, default));
        Assert.NotNull(await db.AddGameAsync());
    }
}

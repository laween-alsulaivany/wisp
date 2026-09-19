using Dapper;
using Microsoft.Data.Sqlite;
using Wisp.Core.Entities;
using Wisp.Data.Repositories;

namespace Wisp.Data.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Wisp.Data.Tests", Guid.NewGuid().ToString("N"));
    internal string DatabasePath => Path.Combine(directory, "wisp.db");
    internal string EstimatesPath => Path.Combine(directory, "completion-estimates #1's.db");
    internal WispDatabase Database { get; }
    internal static DateTimeOffset Now => new(2026, 9, 19, 12, 30, 15, 123, TimeSpan.Zero);
    internal GameRepository Games => new(Database);
    internal SessionRepository Sessions => new(Database);
    internal FeedbackRepository Feedback => new(Database);
    internal SteamProfileRepository Profiles => new(Database);
    internal SettingsRepository Settings => new(Database);
    internal GameStateRepository States => new(Database);

    private TestDatabase() => Database = new WispDatabase(DatabasePath, EstimatesPath);

    internal static async Task<TestDatabase> CreateAsync(bool migrate = true)
    {
        var db = new TestDatabase();
        Directory.CreateDirectory(db.directory);
        await using (var estimates = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = db.EstimatesPath, Pooling = false, DefaultTimeout = 5
        }.ToString()))
        {
            await estimates.OpenAsync();
            await using var stream = typeof(MigrationRunner).Assembly.GetManifestResourceStream(
                "Wisp.Data.Migrations.Bundled.0001_completion_estimates.sql")!;
            using var reader = new StreamReader(stream);
            await estimates.ExecuteAsync(await reader.ReadToEndAsync());
            await estimates.ExecuteAsync("""
                INSERT INTO CompletionEstimates VALUES (420, 100, NULL, 250, 'test-v1');
                """);
        }
        if (migrate)
        {
            await new MigrationRunner(db.Database).ApplyAsync(default);
            await db.ExecuteAsync("INSERT INTO TagDictionary VALUES (10, 'Action'), (20, 'Story'), (30, 'Puzzle');");
        }
        return db;
    }

    internal Task<int> AddProfileAsync(string id = "76561198000000001") => Profiles.UpsertAsync(new SteamProfile
    {
        SteamId64 = id, AccountName = "Account", PersonaName = "Player", LastSeenUtc = Now
    }, default);

    internal async Task<Game> AddGameAsync(long appId = 420)
    {
        await Games.UpsertAsync(new Game { AppId = appId, Name = "Game", Installed = true }, default);
        return (await Games.GetByAppIdAsync(appId, default))!;
    }

    internal Task<int> AddSessionAsync(int gameId, int profileId) => Sessions.StartSessionAsync(new Session
    {
        GameId = gameId, ProfileId = profileId, StartUtc = Now
    }, default);

    internal Task<int> ExecuteAsync(string sql) => Database.WriteAsync(connection => connection.ExecuteAsync(sql), default);

    internal async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await Database.OpenReadAsync(default);
        return (await connection.ExecuteScalarAsync<T>(sql))!;
    }

    public async ValueTask DisposeAsync()
    {
        // Clear only this test's pools; other tests use different files in parallel.
        await using (var read = await Database.OpenReadAsync(default))
            SqliteConnection.ClearPool(read);
        await Database.WriteAsync(connection =>
        {
            SqliteConnection.ClearPool(connection);
            return Task.FromResult(0);
        }, default);
        Directory.Delete(directory, recursive: true);
    }
}

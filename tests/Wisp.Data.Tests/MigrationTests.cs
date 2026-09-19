using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using Wisp.Core.Dtos;
using Wisp.Data.Repositories;
using Xunit;

namespace Wisp.Data.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task AllMigrationsAreAppliedOnceAndBothVersionRecordsAgree()
    {
        await using var db = await TestDatabase.CreateAsync(false);
        var bundledHash = SHA256.HashData(await File.ReadAllBytesAsync(db.EstimatesPath));
        IMigrationRunner runner = new MigrationRunner(db.Database);
        await runner.ApplyAsync(default);
        var schema = await db.ScalarAsync<string>("SELECT group_concat(sql, '|') FROM sqlite_master ORDER BY name;");
        var applied = await db.ScalarAsync<string>("SELECT AppliedUtc FROM SchemaMigrations WHERE Version = 1;");
        await db.AddProfileAsync();

        await runner.ApplyAsync(default);

        Assert.Equal(1, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM SchemaMigrations;"));
        Assert.Equal(applied, await db.ScalarAsync<string>("SELECT AppliedUtc FROM SchemaMigrations WHERE Version = 1;"));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", applied);
        Assert.Equal(schema, await db.ScalarAsync<string>("SELECT group_concat(sql, '|') FROM sqlite_master ORDER BY name;"));
        Assert.Single(await db.Profiles.GetAllAsync(default));
        Assert.Equal(10, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"));
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM main.sqlite_master WHERE name = 'CompletionEstimates';"));
        Assert.Equal(bundledHash, SHA256.HashData(await File.ReadAllBytesAsync(db.EstimatesPath)));
    }

    [Fact]
    public async Task FailedMigrationRollsBackSchemaAndVersionTogether()
    {
        await using var db = await TestDatabase.CreateAsync(false);
        await db.ExecuteAsync("CREATE TABLE SteamProfiles (ProfileId INTEGER PRIMARY KEY);");

        await Assert.ThrowsAsync<SqliteException>(() => new MigrationRunner(db.Database).ApplyAsync(default));

        Assert.Equal(0, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Games', 'SchemaMigrations', 'Sessions');"));
    }

    [Theory]
    [InlineData("PRAGMA user_version = 0;")]
    [InlineData("PRAGMA user_version = 99;")]
    [InlineData("DELETE FROM SchemaMigrations;")]
    public async Task InconsistentOrFutureVersionsAreRejected(string corruption)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ExecuteAsync(corruption);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MigrationRunner(db.Database).ApplyAsync(default));
    }

    [Fact]
    public async Task EstimatesAreAttachedReadOnlyAndQueriedAcrossConnectionLeases()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = new CompletionEstimateRepository(db.Database);
        var expected = new CompletionEstimate(420, 100, null, 250, "test-v1");

        Assert.Equal(expected, await repository.GetAsync(420, default));
        Assert.Equal(expected, await repository.GetAsync(420, default));
        Assert.Null(await repository.GetAsync(999, default));
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE estimates.CompletionEstimates SET MainStoryMinutes = 0;"));
        Assert.Equal(8, error.SqliteErrorCode); // SQLITE_READONLY
    }

    [Fact]
    public async Task EveryConnectionUsesWalForeignKeysAndBusyTimeout()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var read = await db.Database.OpenReadAsync(default);
        await VerifyAsync(read);
        await db.Database.WriteAsync(async write => { await VerifyAsync(write); return 0; }, default);
        var error = await Assert.ThrowsAsync<SqliteException>(() => read.ExecuteAsync(
            "INSERT INTO TagDictionary VALUES (99, 'Forbidden read write');"));
        Assert.Equal(8, error.SqliteErrorCode);

        static async Task VerifyAsync(SqliteConnection connection)
        {
            Assert.Equal(5, new SqliteConnectionStringBuilder(connection.ConnectionString).DefaultTimeout);
            Assert.Equal(5000, await connection.ExecuteScalarAsync<int>("PRAGMA busy_timeout;"));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys;"));
            Assert.Equal("wal", await connection.ExecuteScalarAsync<string>("PRAGMA main.journal_mode;"));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("PRAGMA main.synchronous;"));
        }
    }
}

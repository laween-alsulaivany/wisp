using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Wisp.Data.Repositories;
using Xunit;

namespace Wisp.Data.Tests;

public sealed class StartupAssetsTests
{
    [Fact]
    public async Task Fresh_database_attaches_shipped_empty_estimates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Wisp.Startup.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "wisp.db");
        var database = new WispDatabase(path,
            Path.Combine(AppContext.BaseDirectory, "assets", "completion-estimates.db"));
        try
        {
            await new MigrationRunner(database).ApplyAsync(default);
            await new TagDictionarySeeder(database).SeedAsync(default);
            var estimates = new CompletionEstimateRepository(database);
            (await estimates.GetAsync(420, default)).Should().BeNull();
            (await estimates.GetAsync(long.MaxValue, default)).Should().BeNull();
        }
        finally
        {
            await database.WriteAsync(connection =>
            {
                SqliteConnection.ClearPool(connection);
                return Task.FromResult(0);
            }, default);
            await using (var read = await database.OpenReadAsync(default))
                SqliteConnection.ClearPool(read);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Startup_seeding_is_idempotent_and_refreshes_existing_databases()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ExecuteAsync("DELETE FROM TagDictionary;");
        var seeder = new TagDictionarySeeder(db.Database);
        var tags = JsonSerializer.Deserialize<Dictionary<int, string>>(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "assets", "tags.json")))!;
        await seeder.SeedAsync(default);
        await seeder.SeedAsync(default);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM TagDictionary;")).Should().Be(tags.Count);

        var entry = tags.First();
        await db.ExecuteAsync($"DELETE FROM TagDictionary WHERE TagId = {entry.Key};");
        await db.ExecuteAsync("UPDATE TagDictionary SET TagName = 'Old release name' WHERE TagId = 19;");
        var migrationCount = await db.ScalarAsync<int>("SELECT COUNT(*) FROM SchemaMigrations;");
        await new MigrationRunner(db.Database).ApplyAsync(default);
        await seeder.SeedAsync(default);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM TagDictionary;")).Should().Be(tags.Count);
        (await db.ScalarAsync<string>($"SELECT TagName FROM TagDictionary WHERE TagId = {entry.Key};"))
            .Should().Be(entry.Value);
        (await db.ScalarAsync<string>("SELECT TagName FROM TagDictionary WHERE TagId = 19;"))
            .Should().Be(tags[19]);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM SchemaMigrations;")).Should().Be(migrationCount);
    }
}

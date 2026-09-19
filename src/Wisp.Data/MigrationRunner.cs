using Dapper;

namespace Wisp.Data;

public sealed class MigrationRunner(WispDatabase database) : IMigrationRunner
{
    public Task ApplyAsync(CancellationToken ct) => database.WriteAsync(async connection =>
    {
        var assembly = typeof(MigrationRunner).Assembly;
        const string prefix = "Wisp.Data.Migrations.";
        var migrations = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                && char.IsDigit(name[prefix.Length]) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => (Name: name, Version: int.Parse(name.Substring(prefix.Length, 4))))
            .OrderBy(migration => migration.Version).ToArray();

        using var transaction = connection.BeginTransaction();
        var version = await connection.ExecuteScalarAsync<int>(WispDatabase.Command(
            "PRAGMA main.user_version;", null, ct, transaction));
        if (version > migrations[^1].Version)
            throw new InvalidOperationException("The database schema is newer than this application.");

        var hasHistory = await connection.ExecuteScalarAsync<bool>(WispDatabase.Command(
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'SchemaMigrations');",
            null, ct, transaction));
        var history = hasHistory
            ? (await connection.QueryAsync<int>(WispDatabase.Command(
                "SELECT Version FROM SchemaMigrations ORDER BY Version;", null, ct, transaction))).ToArray()
            : [];
        if (!history.SequenceEqual(migrations.Where(m => m.Version <= version).Select(m => m.Version)))
            throw new InvalidOperationException("SchemaMigrations and PRAGMA user_version disagree.");

        foreach (var migration in migrations.Where(m => m.Version > version))
        {
            await using var stream = assembly.GetManifestResourceStream(migration.Name)!;
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);
            await connection.ExecuteAsync(WispDatabase.Command(sql, null, ct, transaction));
            await connection.ExecuteAsync(WispDatabase.Command("""
                INSERT INTO SchemaMigrations (Version, AppliedUtc) VALUES (@Version, @AppliedUtc);
                """, new { migration.Version, AppliedUtc = DateTimeOffset.UtcNow }, ct, transaction));
            await connection.ExecuteAsync(WispDatabase.Command(
                $"PRAGMA main.user_version = {migration.Version};", null, ct, transaction));
        }

        transaction.Commit();
        return 0;
    }, ct);
}

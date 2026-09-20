using System.Text.Json;
using Dapper;

namespace Wisp.Data;

public sealed class TagDictionarySeeder(WispDatabase database)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "assets", "tags.json"));
        var tags = await JsonSerializer.DeserializeAsync<Dictionary<int, string>>(stream, cancellationToken: ct)
            ?? throw new InvalidDataException("Bundled tag dictionary is empty.");
        await database.WriteAsync(async connection =>
        {
            using var transaction = connection.BeginTransaction();
            foreach (var (id, name) in tags)
            {
                await connection.ExecuteAsync(WispDatabase.Command("""
                    INSERT INTO TagDictionary (TagId, TagName) VALUES (@Id, @Name)
                    ON CONFLICT(TagId) DO UPDATE SET TagName = excluded.TagName
                    WHERE TagName <> excluded.TagName;
                    """, new { Id = id, Name = name }, ct, transaction));
            }
            // Keep older IDs: existing GameTags rows may still reference them.
            transaction.Commit();
            return 0;
        }, ct);
    }
}

using Dapper;
using Microsoft.Data.Sqlite;

namespace Wisp.Data;

/// <summary>Shared connection policy. Run migrations before using repositories.</summary>
public sealed class WispDatabase
{
    private readonly string databasePath;
    private readonly string estimatesUri;

    public WispDatabase(string databasePath, string completionEstimatesPath)
    {
        // A file URI enables URI handling for subsequent ATTACH statements too.
        this.databasePath = new Uri(Path.GetFullPath(databasePath)).AbsoluteUri;
        estimatesUri = new Uri(Path.GetFullPath(completionEstimatesPath)).AbsoluteUri + "?mode=ro";
    }

    internal Task<SqliteConnection> OpenReadAsync(CancellationToken ct) => OpenAsync(false, ct);

    internal Task<T> WriteAsync<T>(Func<SqliteConnection, Task<T>> operation, CancellationToken ct) =>
        SqliteWriteQueue.EnqueueAsync(async () =>
        {
            await using var connection = await OpenAsync(true, ct).ConfigureAwait(false);
            return await operation(connection).ConfigureAwait(false);
        }, ct);

    internal static CommandDefinition Command(string sql, object? parameters, CancellationToken ct,
        SqliteTransaction? transaction = null) =>
        new(sql, parameters is null ? null : SqliteValues.Parameters(parameters), transaction,
            cancellationToken: ct);

    private async Task<SqliteConnection> OpenAsync(bool writable, CancellationToken ct)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = writable ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly,
            DefaultTimeout = 5,
            ForeignKeys = true,
            Pooling = true
        }.ToString());

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await connection.ExecuteAsync(Command("""
                PRAGMA busy_timeout = 5000;
                PRAGMA foreign_keys = ON;
                PRAGMA main.journal_mode = WAL;
                PRAGMA main.synchronous = NORMAL;
                """, null, ct)).ConfigureAwait(false);

            // Native pooled connections can retain attached databases between leases.
            var attached = await connection.QueryAsync<string>(Command(
                "SELECT name FROM pragma_database_list WHERE name = 'estimates';", null, ct)).ConfigureAwait(false);
            if (attached.Any())
                await connection.ExecuteAsync(Command("DETACH DATABASE estimates;", null, ct)).ConfigureAwait(false);
            await connection.ExecuteAsync(Command("ATTACH DATABASE @Path AS estimates;",
                new { Path = estimatesUri }, ct)).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

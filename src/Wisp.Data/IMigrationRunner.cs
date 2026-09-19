namespace Wisp.Data;

public interface IMigrationRunner
{
    Task ApplyAsync(CancellationToken ct);
}

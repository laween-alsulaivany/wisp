namespace Wisp.Core.Interfaces;

public interface IMetadataSyncService
{
    Task RunStartupSyncAsync(CancellationToken ct);
    Task RunDeltaSyncAsync(CancellationToken ct);
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wisp.Core.Interfaces;

namespace Wisp.App.Services.Hosting;

public sealed class MetadataSyncWorker(IMetadataSyncService sync, ILogger<MetadataSyncWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Metadata sync worker started");
        await sync.RunStartupSyncAsync(stoppingToken);
        logger.LogInformation("Metadata startup sync completed");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await sync.RunDeltaSyncAsync(stoppingToken);
            logger.LogInformation("Metadata delta sync completed");
        }
    }
}

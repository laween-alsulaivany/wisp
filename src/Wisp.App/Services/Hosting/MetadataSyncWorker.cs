using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wisp.Core.Interfaces;

namespace Wisp.App.Services.Hosting;

public sealed class MetadataSyncWorker(IMetadataSyncService sync, ILogger<MetadataSyncWorker> logger)
    : BackgroundService
{
    private readonly TaskCompletionSource startupCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task StartupCompleted => startupCompleted.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Metadata sync worker started");
        try
        {
            await sync.RunStartupSyncAsync(stoppingToken);
            startupCompleted.TrySetResult();
        }
        catch (Exception exception)
        {
            startupCompleted.TrySetException(exception);
            throw;
        }
        logger.LogInformation("Metadata startup sync completed");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await sync.RunDeltaSyncAsync(stoppingToken);
            logger.LogInformation("Metadata delta sync completed");
        }
    }
}

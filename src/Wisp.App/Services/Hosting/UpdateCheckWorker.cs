using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wisp.Core.Interfaces;

namespace Wisp.App.Services.Hosting;

public sealed class UpdateCheckWorker(IUpdateChecker checker, UpdateStatus status, ILogger<UpdateCheckWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Update checker worker started");
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                var result = await checker.CheckForUpdateAsync(stoppingToken);
                status.Set(result);
                logger.LogInformation("Update check completed; available: {UpdateAvailable}", result.UpdateAvailable);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
                or JsonException or InvalidOperationException or KeyNotFoundException or IOException)
            {
                logger.LogWarning(exception, "Update check unavailable; next attempt in 24 hours");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

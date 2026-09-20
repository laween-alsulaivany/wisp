using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wisp.Data;

namespace Wisp.App.Services.Hosting;

public sealed class DatabaseStartupService(
    IMigrationRunner migrations, TagDictionarySeeder tags,
    ILogger<DatabaseStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await migrations.ApplyAsync(cancellationToken);
        await tags.SeedAsync(cancellationToken);
        logger.LogInformation("Database migrations and startup tag upsert completed");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

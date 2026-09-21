using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wisp.Data;
using Wisp.Core.Interfaces;

namespace Wisp.App.Services.Hosting;

public sealed class DatabaseStartupService(
    IMigrationRunner migrations, TagDictionarySeeder tags, ISteamProfileRepository profiles,
    ILogger<DatabaseStartupService> logger) : IHostedService
{
    public bool IsFirstLaunch { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await migrations.ApplyAsync(cancellationToken);
        await tags.SeedAsync(cancellationToken);
        // Capture before any background worker can create the first profile.
        IsFirstLaunch = (await profiles.GetAllAsync(cancellationToken)).Count == 0;
        logger.LogInformation("Database migrations and startup tag upsert completed");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

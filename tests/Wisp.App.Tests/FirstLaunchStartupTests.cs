using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Wisp.App.Services.Hosting;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;
using Wisp.Data;
using Xunit;

namespace Wisp.App.Tests;

public sealed class FirstLaunchStartupTests
{
    [Fact]
    public async Task FreshDatabaseShowsWelcomeAndAnyPersistedProfileSkipsItOnNextStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Wisp.FirstLaunch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var registrations = new ServiceCollection();
            registrations.AddLogging();
            AppHost.AddServices(registrations, new AppPaths(directory));
            using var services = registrations.BuildServiceProvider();
            var startup = services.GetRequiredService<DatabaseStartupService>();
            var workers = services.GetServices<IHostedService>().ToList();
            Assert.True(workers.IndexOf(startup) < workers.FindIndex(service => service is SessionTrackingService));
            Assert.True(workers.IndexOf(startup) < workers.FindIndex(service => service is MetadataSyncWorker));
            await startup.StartAsync(default);
            Assert.True(startup.IsFirstLaunch);
            var profiles = services.GetRequiredService<ISteamProfileRepository>();
            await profiles.UpsertAsync(new SteamProfile
            {
                SteamId64 = "76561197960265729", AccountName = "fixture", LastSeenUtc = DateTimeOffset.UnixEpoch
            }, default);
            Assert.True(startup.IsFirstLaunch);
            var nextStartup = new DatabaseStartupService(services.GetRequiredService<IMigrationRunner>(),
                services.GetRequiredService<TagDictionarySeeder>(), profiles,
                NullLogger<DatabaseStartupService>.Instance);
            await nextStartup.StartAsync(default);
            Assert.False(nextStartup.IsFirstLaunch);
            Assert.Single(await profiles.GetAllAsync(default));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}

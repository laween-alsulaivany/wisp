using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Wisp.Core.Interfaces;
using Wisp.App.ViewModels;
using Wisp.Data;
using Wisp.Data.Repositories;
using Wisp.SteamIntegration.AppInfo;
using Wisp.SteamIntegration.Artwork;
using Wisp.SteamIntegration.Manifests;
using Wisp.SteamIntegration.Metadata;
using Wisp.SteamIntegration.Win32;

namespace Wisp.App.Services.Hosting;

public static class AppHost
{
    public static IHost Build(AppPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        return new HostBuilder()
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            })
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                // Deliberately no configuration-driven sinks or default console/event-log providers.
                services.AddSerilog((_, configuration) => configuration
                    .MinimumLevel.Information()
                    .WriteTo.File(paths.LogPath, rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7, fileSizeLimitBytes: 10_000_000,
                        rollOnFileSizeLimit: true));
                AddServices(services, paths);
            }).Build();
    }

    public static void AddServices(IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(_ => new WispDatabase(paths.DatabasePath, paths.CompletionEstimatesPath));
        services.AddSingleton<IMigrationRunner, MigrationRunner>();
        services.AddSingleton<TagDictionarySeeder>();
        services.AddSingleton<IGameRepository, GameRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IFeedbackRepository, FeedbackRepository>();
        services.AddSingleton<IGameStateRepository, GameStateRepository>();
        services.AddSingleton<ICompletionEstimateRepository, CompletionEstimateRepository>();
        services.AddSingleton<ISteamProfileRepository, SteamProfileRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IRecommendationSnapshotProvider, RecommendationSnapshotProvider>();
        services.AddSingleton<ISteamLocator, SteamLocator>();
        services.AddSingleton<ILibraryFoldersParser, LibraryFoldersParser>();
        services.AddSingleton<IAcfManifestParser, AcfManifestParser>();
        services.AddSingleton<ILocalConfigParser, LocalConfigParser>();
        services.AddSingleton<IAppInfoParser, AppInfoParser>();
        services.AddSingleton<ITagDictionaryProvider, TagDictionaryProvider>();
        services.AddSingleton<IMetadataSyncService, MetadataSyncService>();
        services.AddKeyedSingleton<HttpClient>("artwork", (_, _) => CreateAnonymousClient());
        services.AddKeyedSingleton<HttpClient>("updates", (_, _) => CreateAnonymousClient());
        services.AddSingleton<IArtworkFetcher>(provider => new ArtworkFetcher(
            provider.GetRequiredKeyedService<HttpClient>("artwork"),
            provider.GetRequiredService<IGameRepository>(), provider.GetRequiredService<IClock>()));
        services.AddSingleton<ForegroundWindowMonitor>();
        services.AddSingleton<IForegroundWindowMonitor>(provider => provider.GetRequiredService<ForegroundWindowMonitor>());
        services.AddSingleton<ISteamWindowTracker, SteamWindowTracker>();
        services.AddSingleton<IHotkeyManager, HotkeyManager>();
        services.AddSingleton<IRecommendationEngine, RecommendationEngine.RecommendationEngine>();
        services.AddSingleton<IGameStateService, GameStateService>();
        services.AddSingleton<IUpdateChecker>(provider => new UpdateChecker(
            provider.GetRequiredKeyedService<HttpClient>("updates"),
            typeof(AppHost).Assembly.GetName().Version!));
        services.AddSingleton<UpdateStatus>();
        services.AddSingleton<IUriLauncher, WindowsUriLauncher>();
        services.AddTransient<RecommendationViewModel>();
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<FirstLaunchViewModel>();
        services.AddSingleton<IStartupRegistrar, StartupRegistrar>();
        services.AddSingleton<SessionTrackingService>();
        services.AddSingleton<ISessionTracker>(provider => provider.GetRequiredService<SessionTrackingService>());
        services.AddHostedService<LocalTraceService>();
        services.AddSingleton<DatabaseStartupService>();
        services.AddHostedService(provider => provider.GetRequiredService<DatabaseStartupService>());
        services.AddSingleton<MetadataSyncWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<MetadataSyncWorker>());
        services.AddHostedService(provider => provider.GetRequiredService<SessionTrackingService>());
        services.AddHostedService<UpdateCheckWorker>();
    }

    private static HttpClient CreateAnonymousClient() => new(new HttpClientHandler
    {
        UseCookies = false,
        UseDefaultCredentials = false,
        AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(20) };
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Wisp.App.Services;
using Wisp.App.Services.Hosting;
using Wisp.App.ViewModels;
using Wisp.App.Views;

namespace Wisp.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private IHost? host;
    private TrayShell? tray;
    private WindowCoordinator? windows;
    private FirstLaunchWindow? firstLaunch;
    private bool exiting;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        try
        {
            host = AppHost.Build(AppPaths.Default);
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() => dispatcher.TryEnqueue(() => _ = ShutdownAsync()));
            windows = new WindowCoordinator(host.Services);
            await host.StartAsync();
            if (lifetime.ApplicationStopping.IsCancellationRequested)
            {
                await ShutdownAsync();
                return;
            }
            void Open(Action action)
            {
                if (firstLaunch is { } welcome) welcome.Activate();
                else action();
            }
            tray = new TrayShell(host.Services.GetRequiredService<UpdateStatus>(),
                host.Services.GetRequiredService<Wisp.Core.Interfaces.IHotkeyManager>(),
                host.Services.GetRequiredService<ILogger<TrayShell>>(), () => Open(windows.OpenRecommendation),
                () => Open(windows.OpenLibrary), () => Open(windows.OpenHistory),
                () => Open(windows.OpenSettings), ShutdownAsync);
            if (host.Services.GetRequiredService<DatabaseStartupService>().IsFirstLaunch)
            {
                var model = host.Services.GetRequiredService<FirstLaunchViewModel>();
                firstLaunch = new FirstLaunchWindow(model);
                firstLaunch.Activate();
                var loading = model.OpenAsync(Task.WhenAll(
                    host.Services.GetRequiredService<MetadataSyncWorker>().StartupCompleted,
                    host.Services.GetRequiredService<SessionTrackingService>().StartupCompleted));
                var started = await firstLaunch.Completion;
                firstLaunch = null;
                await loading;
                if (!started || exiting) { await ShutdownAsync(); return; }
            }
            windows.StartSteamButton();
        }
        catch (Exception exception)
        {
            host?.Services.GetRequiredService<ILogger<App>>().LogCritical(exception, "Wisp startup failed");
            await ShutdownAsync();
        }
    }

    private async Task ShutdownAsync()
    {
        if (exiting) return;
        exiting = true;
        firstLaunch?.Close();
        try
        {
            if (windows is not null) await windows.ShutdownAsync();
            if (host is not null) await host.StopAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception exception)
        {
            host?.Services.GetRequiredService<ILogger<App>>().LogError(exception, "Wisp shutdown failed");
        }
        finally
        {
            tray?.Dispose();
            windows?.Dispose();
            host?.Dispose();
            Exit();
        }
    }
}

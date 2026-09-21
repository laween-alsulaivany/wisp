using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Wisp.App.Services;
using Wisp.App.Services.Hosting;

namespace Wisp.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private IHost? host;
    private TrayShell? tray;
    private WindowCoordinator? windows;
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
            tray = new TrayShell(host.Services.GetRequiredService<UpdateStatus>(),
                host.Services.GetRequiredService<Wisp.Core.Interfaces.IHotkeyManager>(),
                host.Services.GetRequiredService<ILogger<TrayShell>>(), windows.OpenRecommendation,
                windows.OpenLibrary, windows.OpenHistory, windows.OpenSettings, ShutdownAsync);
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

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wisp.App.Services.Hosting;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class ContainerValidationTests
{
    [Fact]
    public void Production_host_resolves_all_registered_interfaces_and_workers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Wisp.Container.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(directory);
        try
        {
            using var host = AppHost.Build(paths);
            var interfaces = typeof(IClock).Assembly.GetTypes().Where(type => type.IsInterface);
            foreach (var contract in interfaces)
                host.Services.GetRequiredService(contract).Should().NotBeNull();
            var registrations = new ServiceCollection();
            AppHost.AddServices(registrations, paths);
            foreach (var descriptor in registrations.Where(d => !d.IsKeyedService && d.ServiceType.IsInterface))
                host.Services.GetRequiredService(descriptor.ServiceType).Should().NotBeNull();
            var workers = host.Services.GetServices<IHostedService>().ToArray();
            workers.Should().HaveCount(5);
            workers.OfType<SessionTrackingService>().Single().Should()
                .BeSameAs(host.Services.GetRequiredService<ISessionTracker>());
            host.Services.GetRequiredService<Wisp.App.ViewModels.RecommendationViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<Wisp.App.ViewModels.LibraryViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<Wisp.App.ViewModels.HistoryViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<Wisp.App.ViewModels.SettingsViewModel>().Should().NotBeNull();
            host.Services.GetRequiredService<IForegroundWindowMonitor>().Should()
                .BeSameAs(host.Services.GetRequiredService<Wisp.SteamIntegration.Win32.ForegroundWindowMonitor>());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}

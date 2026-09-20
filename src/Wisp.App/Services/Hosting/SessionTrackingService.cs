using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Sessions;
using Wisp.SteamIntegration.Win32;

namespace Wisp.App.Services.Hosting;

// The Phase 7 tracker owns one profile. This host adapter owns profile discovery and lifetime.
public sealed class SessionTrackingService(
    ISteamLocator locator, ISteamProfileRepository profiles, IGameRepository games,
    ISessionRepository sessions, ForegroundWindowMonitor foreground, ISteamWindowTracker steamWindow,
    IClock clock, ILogger<SessionTrackingService> logger) : BackgroundService, ISessionTracker
{
    private readonly object gate = new();
    private SessionTracker? tracker;
    private bool enabled = true;

    public event EventHandler<SessionStartedEventArgs>? SessionStarted;
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    public void Start() { lock (gate) { enabled = true; tracker?.Start(); } }
    public void Stop()
    {
        SessionTracker? current;
        lock (gate) { enabled = false; current = tracker; }
        current?.Stop();
    }
    public void NotifyRecommendationLaunch(long appId)
    {
        lock (gate) tracker?.NotifyRecommendationLaunch(appId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = steamWindow; // Attach both consumers before starting their shared foreground monitor.
        foreground.Start();
        logger.LogInformation("Session tracking worker started");
        string? currentSteamId = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                var account = string.Empty;
                var steamId = locator.TryGetSteamInstallPath(out var path)
                    && locator.TryGetActiveSteamId3(path, out var id3, out account)
                    ? (76561197960265728UL + uint.Parse(id3, CultureInfo.InvariantCulture))
                        .ToString(CultureInfo.InvariantCulture) : null;
                if (steamId != currentSteamId)
                {
                    ReleaseTracker();
                    if (steamId is not null)
                    {
                        var existing = await profiles.GetBySteamId64Async(steamId, stoppingToken);
                        var profileId = await profiles.UpsertAsync((existing ?? new SteamProfile()) with
                        {
                            SteamId64 = steamId, AccountName = account, LastSeenUtc = clock.UtcNow
                        }, stoppingToken);
                        lock (gate)
                        {
                            tracker = new SessionTracker(profileId, games, sessions, foreground, clock);
                            tracker.SessionStarted += ForwardStarted;
                            tracker.SessionEnded += ForwardEnded;
                            if (enabled) tracker.Start();
                        }
                        logger.LogInformation("Session tracker started for active local profile");
                    }
                    currentSteamId = steamId;
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally { ReleaseTracker(); }
    }

    private void ForwardStarted(object? sender, SessionStartedEventArgs args) => SessionStarted?.Invoke(this, args);
    private void ForwardEnded(object? sender, SessionEndedEventArgs args) => SessionEnded?.Invoke(this, args);

    private void ReleaseTracker()
    {
        SessionTracker? previous;
        lock (gate) { previous = tracker; tracker = null; }
        if (previous is null) return;
        previous.Dispose();
        previous.SessionStarted -= ForwardStarted;
        previous.SessionEnded -= ForwardEnded;
    }
}

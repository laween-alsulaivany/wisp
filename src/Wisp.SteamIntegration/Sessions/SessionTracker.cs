using System.Diagnostics;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Sessions;

/// <summary>Events run on the polling worker; UI consumers must dispatch to their UI thread.</summary>
public sealed class SessionTracker : ISessionTracker, IDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan RestartGroupingWindow = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan RecommendationLaunchAttributionWindow = TimeSpan.FromSeconds(30);

    private readonly int profileId;
    private readonly IGameRepository games;
    private readonly ISessionRepository sessions;
    private readonly IForegroundWindowMonitor foreground;
    private readonly IClock clock;
    private readonly IProcessSnapshotProvider processes;
    private readonly TimeProvider timerTime;
    private readonly object gate = new();
    private readonly SemaphoreSlim pollGate = new(1, 1);
    // A missing entry is Idle. Each entry owns all processes in one game's directory.
    private readonly Dictionary<int, TrackedGame> tracked = [];
    private readonly Dictionary<long, DateTimeOffset> launches = [];
    private int? foregroundPid;
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private bool disposed;

    // The composition root supplies the profile whose sessions this tracker owns.
    public SessionTracker(int profileId, IGameRepository games, ISessionRepository sessions,
        IForegroundWindowMonitor foreground, IClock clock)
        : this(profileId, games, sessions, foreground, clock, new ProcessSnapshotProvider()) { }

    internal SessionTracker(int profileId, IGameRepository games, ISessionRepository sessions,
        IForegroundWindowMonitor foreground, IClock clock, IProcessSnapshotProvider processes,
        TimeProvider? timerTime = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profileId);
        this.profileId = profileId;
        this.games = games;
        this.sessions = sessions;
        this.foreground = foreground;
        this.clock = clock;
        this.processes = processes;
        this.timerTime = timerTime ?? TimeProvider.System;
        foregroundPid = foreground.CurrentForegroundProcessId;
        foreground.ForegroundProcessChanged += OnForegroundChanged;
    }

    public event EventHandler<SessionStartedEventArgs>? SessionStarted;
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    public void NotifyRecommendationLaunch(long appId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            launches[appId] = clock.UtcNow;
        }
    }

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is not null)
                return;
            cancellation = new();
            var token = cancellation.Token;
            worker = Task.Run(() => RunAsync(token));
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval, timerTime);
        try
        {
            do
            {
                await PollAsync(ct).ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Trace.TraceError($"Session tracking stopped: {exception}");
            throw;
        }
    }

    internal async Task PollAsync(CancellationToken ct = default)
    {
        await pollGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var installed = (await games.GetAllAsync(ct).ConfigureAwait(false))
                .Where(game => game.Installed && !string.IsNullOrWhiteSpace(game.InstallDir))
                .ToDictionary(game => game.GameId);
            var snapshot = processes.GetProcesses();
            DateTimeOffset now;
            lock (gate)
            {
                now = clock.UtcNow;
                Accumulate(now);
                foreach (var appId in launches.Where(pair => now - pair.Value >= RecommendationLaunchAttributionWindow)
                    .Select(pair => pair.Key).ToArray())
                    launches.Remove(appId);
            }

            foreach (var gameId in tracked.Keys.Union(installed.Keys).ToArray())
            {
                TrackedGame? current;
                lock (gate) current = tracked.GetValueOrDefault(gameId);
                var directory = current?.Directory ?? DirectoryPrefix(installed[gameId].InstallDir!);
                var pids = snapshot.Where(process => process.ExecutablePath.StartsWith(directory,
                    StringComparison.OrdinalIgnoreCase)).Select(process => process.ProcessId).ToHashSet();

                if (current is not null && (current.State == TrackingState.Finalizing
                    || current.State == TrackingState.PendingRestart && now - current.ClosedUtc >= RestartGroupingWindow))
                {
                    await FinalizeAsync(current, ct).ConfigureAwait(false);
                    current = null;
                }

                if (current is null)
                {
                    if (pids.Count == 0 || !installed.TryGetValue(gameId, out var game))
                        continue;
                    ct.ThrowIfCancellationRequested();
                    lock (gate)
                    {
                        now = clock.UtcNow;
                        Accumulate(now);
                        var source = launches.Remove(game.AppId, out var notified)
                            && now >= notified && now - notified < RecommendationLaunchAttributionWindow
                            ? LaunchSource.Recommendation : LaunchSource.Manual;
                        current = new TrackedGame(new Session
                        {
                            GameId = gameId, ProfileId = profileId, StartUtc = now, LaunchSource = source
                        }, directory, pids, now);
                        tracked.Add(gameId, current);
                    }
                    try
                    {
                        // Stop waits for committed transitions instead of canceling half of a session change.
                        var id = await sessions.StartSessionAsync(current.Session, CancellationToken.None).ConfigureAwait(false);
                        lock (gate) current.Session = current.Session with { SessionId = id };
                    }
                    catch
                    {
                        lock (gate) tracked.Remove(gameId);
                        throw;
                    }
                    SessionStarted?.Invoke(this, new(current.Session));
                }
                else if (current.State == TrackingState.PendingRestart && pids.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    DateTimeOffset closed;
                    lock (gate)
                    {
                        now = clock.UtcNow;
                        Accumulate(now);
                        closed = current.ClosedUtc!.Value;
                        current.Session = current.Session with { RestartCount = current.Session.RestartCount + 1 };
                        current.ClosedUtc = null;
                        current.State = TrackingState.Tracking;
                        current.Pids = pids;
                        current.LastAccountedUtc = now;
                    }
                    await sessions.RecordRestartAsync(current.Session.SessionId, closed, now, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    lock (gate)
                    {
                        now = clock.UtcNow;
                        Accumulate(now);
                        current.Pids = pids;
                        if (current.State == TrackingState.Tracking && pids.Count == 0)
                        {
                            current.State = TrackingState.PendingRestart;
                            current.ClosedUtc = now;
                        }
                    }
                }
            }
        }
        finally { pollGate.Release(); }
    }

    private static string DirectoryPrefix(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;

    private void OnForegroundChanged(object? sender, ForegroundProcessChangedEventArgs args)
    {
        lock (gate)
        {
            Accumulate(clock.UtcNow);
            foregroundPid = args.ProcessId;
        }
    }

    private void Accumulate(DateTimeOffset now)
    {
        foreach (var game in tracked.Values)
        {
            if (game.State == TrackingState.Tracking && foregroundPid is { } pid && game.Pids.Contains(pid))
            {
                var elapsed = now - game.LastAccountedUtc;
                game.ForegroundTime += elapsed;
                game.TimeByPid[pid] = game.TimeByPid.GetValueOrDefault(pid) + elapsed;
            }
            game.LastAccountedUtc = now;
        }
    }

    internal int? GetPrimaryProcessId(int gameId)
    {
        lock (gate)
            return tracked.TryGetValue(gameId, out var game)
                ? game.TimeByPid.OrderByDescending(pair => pair.Value).Select(pair => (int?)pair.Key).FirstOrDefault()
                : null;
    }

    private async Task FinalizeAsync(TrackedGame game, CancellationToken ct)
    {
        Session completed;
        lock (gate)
        {
            game.State = TrackingState.Finalizing;
            var end = game.ClosedUtc!.Value;
            completed = game.Session with
            {
                EndUtc = end,
                RuntimeSeconds = (int)(end - game.Session.StartUtc).TotalSeconds,
                ActiveForegroundSeconds = (int)game.ForegroundTime.TotalSeconds
            };
        }
        await sessions.CompleteSessionAsync(completed.SessionId, completed.EndUtc!.Value,
            completed.RuntimeSeconds!.Value, completed.ActiveForegroundSeconds, ct).ConfigureAwait(false);
        lock (gate) tracked.Remove(completed.GameId);
        SessionEnded?.Invoke(this, new(completed));
    }

    public void Stop()
    {
        Task? running;
        lock (gate)
        {
            cancellation?.Cancel();
            running = worker;
        }
        try { running?.GetAwaiter().GetResult(); }
        finally
        {
            FinishStoppedSessionsAsync().GetAwaiter().GetResult();
            lock (gate)
            {
                cancellation?.Dispose();
                cancellation = null;
                worker = null;
                launches.Clear();
            }
        }
    }

    private async Task FinishStoppedSessionsAsync()
    {
        await pollGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                var now = clock.UtcNow;
                Accumulate(now);
                foreach (var game in tracked.Values)
                {
                    game.ClosedUtc ??= now;
                    game.State = TrackingState.Finalizing;
                }
            }
            foreach (var game in tracked.Values.ToArray())
                await FinalizeAsync(game, CancellationToken.None).ConfigureAwait(false);
        }
        finally { pollGate.Release(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        foreground.ForegroundProcessChanged -= OnForegroundChanged;
        disposed = true;
        pollGate.Dispose();
    }

    private enum TrackingState { Tracking, PendingRestart, Finalizing }

    private sealed class TrackedGame(Session session, string directory, HashSet<int> pids, DateTimeOffset now)
    {
        public Session Session = session;
        public string Directory = directory;
        public HashSet<int> Pids = pids;
        public TrackingState State = TrackingState.Tracking;
        public DateTimeOffset? ClosedUtc;
        public DateTimeOffset LastAccountedUtc = now;
        public TimeSpan ForegroundTime;
        public Dictionary<int, TimeSpan> TimeByPid = [];
    }
}

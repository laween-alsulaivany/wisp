using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Sessions;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class SessionTrackerTests
{
    [Fact]
    public async Task RestartAtFourMinutesContinuesOneSessionAndRecordsRestart()
    {
        using var fixture = new Fixture();
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await fixture.Run();
        var closed = fixture.Clock.UtcNow;
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));
        await fixture.Run(101);

        Assert.Single(fixture.Started);
        Assert.Empty(fixture.Ended);
        var restart = Assert.Single(fixture.Repository.Restarts);
        Assert.Equal((1, closed, fixture.Clock.UtcNow), restart);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.Run();
        var end = fixture.Clock.UtcNow;
        fixture.Clock.Advance(SessionTracker.RestartGroupingWindow);
        await fixture.Run();
        await fixture.Run();

        var session = Assert.Single(fixture.Ended);
        Assert.Equal(1, session.SessionId);
        Assert.Equal(1, session.RestartCount);
        Assert.Equal(end, session.EndUtc);
        Assert.Equal(420, session.RuntimeSeconds);
        Assert.Single(fixture.Repository.Rows);
        Assert.Single(fixture.Repository.Completions);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task RelaunchAtOrAfterExpiryCreatesTwoLogicalSessions(int minutes)
    {
        using var fixture = new Fixture();
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.Run();
        var closed = fixture.Clock.UtcNow;
        fixture.Clock.Advance(TimeSpan.FromMinutes(minutes));
        await fixture.Run(101);

        Assert.Equal(2, fixture.Started.Count);
        Assert.Equal(closed, Assert.Single(fixture.Ended).EndUtc);
        Assert.Empty(fixture.Repository.Restarts);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.Run();
        fixture.Clock.Advance(SessionTracker.RestartGroupingWindow);
        await fixture.Run();
        await fixture.Run();

        Assert.Equal(new[] { 1, 2 }, fixture.Started.Select(session => session.SessionId));
        Assert.Equal(new[] { 1, 2 }, fixture.Ended.Select(session => session.SessionId));
        Assert.Equal(2, fixture.Repository.Rows.Count);
        Assert.Equal(2, fixture.Repository.Completions.Count);
    }

    [Fact]
    public async Task ForegroundEventsPauseForUnrelatedApplicationAndResumeWithoutPolling()
    {
        using var fixture = new Fixture();
        fixture.Foreground.Change(100);
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        fixture.Foreground.Change(999);
        fixture.Clock.Advance(TimeSpan.FromSeconds(40));
        fixture.Foreground.Change(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        fixture.Foreground.Change(null);
        fixture.Clock.Advance(TimeSpan.FromSeconds(15));
        await fixture.Run();
        fixture.Clock.Advance(SessionTracker.RestartGroupingWindow);
        await fixture.Run();

        var ended = Assert.Single(fixture.Ended);
        Assert.Equal(30, ended.ActiveForegroundSeconds);
        Assert.Equal(85, ended.RuntimeSeconds);
        Assert.Equal(30, fixture.Repository.Rows[ended.SessionId].ActiveForegroundSeconds);
    }

    [Fact]
    public async Task DirectoryRemainsRunningWhenLauncherExitsAndPrimaryFollowsForegroundTime()
    {
        using var fixture = new Fixture();
        await fixture.Run(100, 101);
        fixture.Foreground.Change(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        fixture.Foreground.Change(101);
        fixture.Clock.Advance(TimeSpan.FromSeconds(12));
        await fixture.Run(101);

        Assert.Equal(101, fixture.Tracker.GetPrimaryProcessId(Fixture.GameId));
        Assert.Empty(fixture.Repository.Restarts);
        Assert.Empty(fixture.Ended);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await fixture.Run();
        fixture.Clock.Advance(SessionTracker.RestartGroupingWindow);
        await fixture.Run();
        Assert.Equal(20, Assert.Single(fixture.Ended).ActiveForegroundSeconds);
        Assert.Single(fixture.Started);
    }

    [Fact]
    public async Task RestartGapDoesNotCountAsForegroundAndFractionalIntervalsArePreserved()
    {
        using var fixture = new Fixture();
        fixture.Foreground.Change(100);
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(750));
        await fixture.Run();
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(750));
        await fixture.Run();
        fixture.Clock.Advance(SessionTracker.RestartGroupingWindow);
        await fixture.Run();

        Assert.Equal(1, Assert.Single(fixture.Ended).ActiveForegroundSeconds);
        Assert.Single(fixture.Started);
        Assert.Single(fixture.Repository.Restarts);
    }

    [Theory]
    [InlineData(10, true, LaunchSource.Recommendation)]
    [InlineData(45, true, LaunchSource.Manual)]
    [InlineData(10, false, LaunchSource.Manual)]
    [InlineData(30, true, LaunchSource.Manual)]
    public async Task LaunchAttributionUsesClockAndThirtySecondWindow(int seconds, bool notify, LaunchSource expected)
    {
        using var fixture = new Fixture();
        if (notify) fixture.Tracker.NotifyRecommendationLaunch(Fixture.AppId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(seconds));
        await fixture.Run(100);
        Assert.Equal(expected, Assert.Single(fixture.Started).LaunchSource);
        Assert.Equal(expected, Assert.Single(fixture.Repository.Rows).Value.LaunchSource);
    }

    [Fact]
    public async Task NotificationIsKeyedByAppIdAndDoesNotAttributeOtherGames()
    {
        using var fixture = new Fixture();
        fixture.Tracker.NotifyRecommendationLaunch(Fixture.AppId + 1);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        await fixture.Run(100);
        Assert.Equal(LaunchSource.Manual, Assert.Single(fixture.Started).LaunchSource);
    }

    [Fact]
    public async Task NotificationIsConsumedAndRestartDoesNotRewriteLaunchSource()
    {
        using var fixture = new Fixture();
        fixture.Tracker.NotifyRecommendationLaunch(Fixture.AppId);
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Run();
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Run(101);
        fixture.Tracker.Stop();
        await fixture.Run(102);

        Assert.Equal(LaunchSource.Recommendation, fixture.Ended[0].LaunchSource);
        Assert.Equal(LaunchSource.Manual, fixture.Started[1].LaunchSource);
        Assert.Equal(2, fixture.Started.Count);
        Assert.Single(fixture.Repository.Restarts);
    }

    [Fact]
    public async Task PathsRequireDirectoryBoundaryAndInstalledFlagButIgnoreCase()
    {
        using var fixture = new Fixture();
        fixture.Games.Rows[999] = new Game
        {
            GameId = 99, AppId = 999, Installed = false, InstallDir = @"D:\Library\steamapps\common\Removed"
        };
        fixture.Processes.Snapshot =
        [
            new(1, Fixture.Directory + @" sequel\game.exe"),
            new(2, @"D:\Library\steamapps\common\Removed\game.exe"),
            new(3, @"C:\unrelated\game.exe")
        ];
        await fixture.Tracker.PollAsync();
        Assert.Empty(fixture.Started);
        fixture.Processes.Snapshot = [new(4, Fixture.Directory.ToUpperInvariant() + @"\bin\game.exe")];
        await fixture.Tracker.PollAsync();
        Assert.Equal(Fixture.GameId, Assert.Single(fixture.Started).GameId);
    }

    [Fact]
    public async Task MultipleGamesHaveIndependentSessionsAndForegroundTotals()
    {
        using var fixture = new Fixture();
        fixture.Games.Rows[999] = new Game
        {
            GameId = 99, AppId = 999, Installed = true, InstallDir = @"E:\OtherLibrary\steamapps\common\Other"
        };
        fixture.Processes.Snapshot =
        [new(100, Fixture.Directory + @"\game.exe"), new(200, @"E:\OtherLibrary\steamapps\common\Other\game.exe")];
        await fixture.Tracker.PollAsync();
        fixture.Foreground.Change(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(12));
        fixture.Foreground.Change(200);
        fixture.Clock.Advance(TimeSpan.FromSeconds(7));
        await fixture.Run();
        fixture.Clock.Advance(SessionTracker.RestartGroupingWindow);
        await fixture.Run();
        Assert.Equal(2, fixture.Started.Count);
        Assert.Equal(12, fixture.Ended.Single(session => session.GameId == Fixture.GameId).ActiveForegroundSeconds);
        Assert.Equal(7, fixture.Ended.Single(session => session.GameId == 99).ActiveForegroundSeconds);
    }

    [Fact]
    public async Task StopCompletesOpenAndPendingSessionsOnceAtTheirActualEnd()
    {
        using var fixture = new Fixture();
        fixture.Foreground.Change(100);
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(12));
        await fixture.Run();
        var closed = fixture.Clock.UtcNow;
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        fixture.Tracker.Stop();
        fixture.Tracker.Stop();
        Assert.Equal(closed, Assert.Single(fixture.Ended).EndUtc);
        Assert.Equal(12, fixture.Ended[0].ActiveForegroundSeconds);
        Assert.Single(fixture.Repository.Completions);
    }

    [Fact]
    public async Task ForegroundSwitchDuringRestartWriteDoesNotCountBackgroundTime()
    {
        using var fixture = new Fixture();
        fixture.Foreground.Change(100);
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.Run();
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));
        fixture.Repository.OnRestart = () =>
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(2));
            fixture.Foreground.Change(999);
            fixture.Clock.Advance(TimeSpan.FromSeconds(8));
        };
        await fixture.Run(100);
        fixture.Foreground.Change(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        await fixture.Run();
        fixture.Clock.Advance(SessionTracker.RestartGroupingWindow);
        await fixture.Run();
        Assert.Equal(10, Assert.Single(fixture.Ended).ActiveForegroundSeconds);
    }

    [Fact]
    public async Task StartUsesOneThreeSecondPeriodicTimerAndStopCancelsIt()
    {
        using var fixture = new Fixture();
        fixture.Processes.Snapshot = [new(100, Fixture.Directory + @"\game.exe")];
        fixture.Foreground.Change(100);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Tracker.SessionStarted += (_, _) => started.SetResult();
        fixture.Tracker.Start();
        fixture.Tracker.Start();
        await started.Task;
        Assert.Single(fixture.Clock.Timers);
        Assert.Equal(SessionTracker.PollInterval, fixture.Clock.Timers[0].Period);

        var nextPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Processes.OnSnapshot = () => nextPoll.TrySetResult();
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(nextPoll.Task.IsCompleted);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await nextPoll.Task;
        fixture.Tracker.Stop();
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.True(fixture.Clock.Timers[0].Disposed);
        Assert.Single(fixture.Started);
        Assert.Equal(3, Assert.Single(fixture.Ended).ActiveForegroundSeconds);
    }

    [Fact]
    public async Task CancellationDuringRestartWriteDoesNotLoseTheRecordedRestart()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await fixture.Run(100);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        await fixture.Run();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Repository.OnRestart = cancellation.Cancel;
        fixture.Processes.Snapshot = [new(101, Fixture.Directory + @"\game.exe")];
        await fixture.Tracker.PollAsync(cancellation.Token);
        fixture.Tracker.Stop();
        Assert.Single(fixture.Repository.Restarts);
        Assert.Equal(1, Assert.Single(fixture.Ended).RestartCount);
        Assert.Equal(1, fixture.Repository.Rows[1].RestartCount);
    }

    private sealed class Fixture : IDisposable
    {
        internal const long AppId = 570;
        internal const int GameId = 7;
        internal const string Directory = @"D:\Library\steamapps\common\Example Game";
        internal FakeClock Clock { get; } = new();
        internal FakeForeground Foreground { get; } = new();
        internal FakeProcesses Processes { get; } = new();
        internal MemorySessions Repository { get; } = new();
        internal MemoryGameRepository Games { get; } = new(new Game
        {
            GameId = GameId, AppId = AppId, Installed = true, InstallDir = Directory
        });
        internal SessionTracker Tracker { get; }
        internal List<Session> Started { get; } = [];
        internal List<Session> Ended { get; } = [];

        internal Fixture()
        {
            Tracker = new(3, Games, Repository, Foreground, Clock, Processes, Clock);
            Tracker.SessionStarted += (_, args) => Started.Add(args.Session);
            Tracker.SessionEnded += (_, args) => Ended.Add(args.Session);
        }

        internal Task Run(params int[] pids)
        {
            Processes.Snapshot = pids.Select(pid => new ProcessSnapshot(pid, Directory + @"\bin\game.exe")).ToArray();
            return Tracker.PollAsync();
        }

        public void Dispose() => Tracker.Dispose();
    }

    private sealed class FakeClock : TimeProvider, IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        internal List<FakeTimer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(this, callback, state, dueTime, period);
            Timers.Add(timer);
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            UtcNow += duration;
            foreach (var timer in Timers.ToArray()) timer.Tick();
        }
    }

    private sealed class FakeTimer(FakeClock clock, TimerCallback callback, object? state,
        TimeSpan dueTime, TimeSpan period) : ITimer
    {
        private DateTimeOffset next = clock.UtcNow + dueTime;
        internal TimeSpan Period { get; private set; } = period;
        internal bool Disposed { get; private set; }

        internal void Tick()
        {
            if (Disposed || clock.UtcNow < next) return;
            next = clock.UtcNow + Period;
            callback(state);
        }

        public bool Change(TimeSpan due, TimeSpan interval)
        {
            next = clock.UtcNow + due;
            Period = interval;
            return !Disposed;
        }

        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class FakeForeground : IForegroundWindowMonitor
    {
        public int? CurrentForegroundProcessId { get; private set; }
        public event EventHandler<ForegroundProcessChangedEventArgs>? ForegroundProcessChanged;
        internal void Change(int? pid)
        {
            CurrentForegroundProcessId = pid;
            ForegroundProcessChanged?.Invoke(this, new(pid));
        }
    }

    private sealed class FakeProcesses : IProcessSnapshotProvider
    {
        internal IReadOnlyList<ProcessSnapshot> Snapshot { get; set; } = [];
        internal Action? OnSnapshot { get; set; }
        public IReadOnlyList<ProcessSnapshot> GetProcesses()
        {
            OnSnapshot?.Invoke();
            return Snapshot;
        }
    }

    private sealed class MemorySessions : ISessionRepository
    {
        internal Dictionary<int, Session> Rows { get; } = [];
        internal List<(int Id, DateTimeOffset Closed, DateTimeOffset Relaunched)> Restarts { get; } = [];
        internal List<int> Completions { get; } = [];
        internal Action? OnRestart { get; set; }

        public Task<int> StartSessionAsync(Session session, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var id = Rows.Count + 1;
            Rows.Add(id, session with { SessionId = id });
            return Task.FromResult(id);
        }

        public Task CompleteSessionAsync(int sessionId, DateTimeOffset endUtc, int runtimeSeconds,
            int activeForegroundSeconds, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Rows[sessionId] = Rows[sessionId] with
            {
                EndUtc = endUtc, RuntimeSeconds = runtimeSeconds, ActiveForegroundSeconds = activeForegroundSeconds
            };
            Completions.Add(sessionId);
            return Task.CompletedTask;
        }

        public Task RecordRestartAsync(int sessionId, DateTimeOffset closedUtc, DateTimeOffset relaunchedUtc, CancellationToken ct)
        {
            OnRestart?.Invoke();
            ct.ThrowIfCancellationRequested();
            Restarts.Add((sessionId, closedUtc, relaunchedUtc));
            Rows[sessionId] = Rows[sessionId] with { RestartCount = Rows[sessionId].RestartCount + 1 };
            return Task.CompletedTask;
        }

        public Task<Session?> GetByIdAsync(int sessionId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Rows.GetValueOrDefault(sessionId));
        }

        public Task<IReadOnlyList<Session>> GetRecentAsync(int profileId, int limit, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearHistoryAsync(int profileId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<PlaytimeDistribution> GetActivePlaytimeDistributionAsync(int profileId, long? appId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

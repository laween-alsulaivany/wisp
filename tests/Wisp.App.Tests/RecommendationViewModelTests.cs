using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wisp.App.ViewModels;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class RecommendationViewModelTests
{
    private readonly IRecommendationEngine engine = Substitute.For<IRecommendationEngine>();
    private readonly IRecommendationSnapshotProvider snapshots = Substitute.For<IRecommendationSnapshotProvider>();
    private readonly ISessionTracker sessions = Substitute.For<ISessionTracker>();
    private readonly IArtworkFetcher artwork = Substitute.For<IArtworkFetcher>();
    private readonly IUriLauncher launcher = Substitute.For<IUriLauncher>();
    private readonly RecommendationInputSnapshot snapshot = new()
    {
        CurrentTimeUtc = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero),
        Candidates = [new() { Game = new() { GameId = 4, AppId = 123, Name = "Fixture" } }]
    };

    public RecommendationViewModelTests()
    {
        snapshots.BuildSnapshotAsync(7, Arg.Any<CancellationToken>()).Returns(snapshot);
        engine.GetRecommendationAsync(Arg.Any<RecommendationRequest>(), Arg.Any<RecommendationInputSnapshot>(),
            Arg.Any<CancellationToken>()).Returns(Result(4, 123));
        launcher.LaunchAsync(Arg.Any<Uri>()).Returns(true);
    }

    private RecommendationViewModel Create() => new(engine, snapshots, sessions, artwork, launcher,
        NullLogger<RecommendationViewModel>.Instance);
    private static RecommendationResult Result(int id, long appId) => new()
    {
        Game = new() { GameId = id, AppId = appId, Name = $"Game {id}" }, Reasons = ["A reason from the engine"]
    };

    [Fact]
    public async Task Reroll_accumulates_game_ids_and_passes_each_snapshot_through_unchanged()
    {
        var requests = new List<RecommendationRequest>();
        engine.GetRecommendationAsync(Arg.Any<RecommendationRequest>(), Arg.Any<RecommendationInputSnapshot>(),
            Arg.Any<CancellationToken>()).Returns(call =>
        {
            requests.Add(call.Arg<RecommendationRequest>());
            Assert.Same(snapshot, call.Arg<RecommendationInputSnapshot>());
            return Result(requests.Count + 3, 1000 + requests.Count);
        });
        using var model = Create();
        await model.OpenAsync(7);
        await model.RerollCommand.ExecuteAsync(null);
        await model.RerollCommand.ExecuteAsync(null);
        Assert.Empty(requests[0].ExcludedGameIdsThisSession);
        Assert.Equal([4], requests[1].ExcludedGameIdsThisSession.Order());
        Assert.Equal([4, 5], requests[2].ExcludedGameIdsThisSession.Order());
        Assert.All(requests, request => Assert.Same(AdvancedFilters.None, request.Advanced));
        Assert.Single(snapshot.Candidates);
        Assert.Equal(4, snapshot.Candidates[0].Game.GameId);
        await snapshots.Received(3).BuildSnapshotAsync(7, Arg.Any<CancellationToken>());
        sessions.DidNotReceive().NotifyRecommendationLaunch(Arg.Any<long>());
    }

    [Fact]
    public async Task Play_notifies_the_displayed_app_id_before_launching_its_uri()
    {
        var calls = new List<string>();
        sessions.When(s => s.NotifyRecommendationLaunch(Arg.Any<long>())).Do(call => calls.Add($"notify:{call.Arg<long>()}"));
        launcher.LaunchAsync(Arg.Any<Uri>()).Returns(call => { calls.Add(call.Arg<Uri>().AbsoluteUri); return true; });
        engine.GetRecommendationAsync(Arg.Any<RecommendationRequest>(), snapshot, Arg.Any<CancellationToken>())
            .Returns(Result(4, 100), Result(8, 4_000_000_001));
        using var model = Create();
        await model.OpenAsync(7);
        await model.RerollCommand.ExecuteAsync(null);
        await model.PlayCommand.ExecuteAsync(null);
        Assert.Equal(["notify:4000000001", "steam://run/4000000001"], calls);
    }

    [Fact]
    public async Task Mood_and_time_changes_make_new_session_only_requests_and_reopening_resets_them()
    {
        var settings = Substitute.For<ISettingsRepository>();
        var persisted = new ProfileSettings { ProfileId = 7, IncludeFinishedGames = true, MaybeLaterCooldownDays = 12 };
        settings.GetAsync(7, Arg.Any<CancellationToken>()).Returns(persisted);
        snapshots.BuildSnapshotAsync(7, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            _ = await settings.GetAsync(7, call.Arg<CancellationToken>());
            return snapshot;
        });
        var requests = new List<RecommendationRequest>();
        engine.GetRecommendationAsync(Arg.Any<RecommendationRequest>(), snapshot, Arg.Any<CancellationToken>())
            .Returns(call => { requests.Add(call.Arg<RecommendationRequest>()); return Result(4, 123); });
        using var model = Create();
        await model.OpenAsync(7);
        await model.RerollCommand.ExecuteAsync(null);
        model.Mood = MoodFilter.HighEnergy;
        await model.RefreshCommand.ExecutionTask!;
        model.Time = new TimeFilter(TimeSpan.FromMinutes(75));
        await model.RefreshCommand.ExecutionTask!;
        Assert.Equal(MoodFilter.HighEnergy, requests[^1].Mood);
        Assert.Equal(TimeSpan.FromMinutes(75), requests[^1].Time.TargetDuration);
        Assert.Equal([4], requests[^1].ExcludedGameIdsThisSession);
        Assert.NotSame(requests[^1], requests[^2]);
        Assert.Null(requests[^2].Time.TargetDuration);
        Assert.Equal(MoodFilter.Anything, requests[0].Mood);
        await model.OpenAsync(7);
        Assert.Equal(MoodFilter.Anything, requests[^1].Mood);
        Assert.Null(requests[^1].Time.TargetDuration);
        Assert.Empty(requests[^1].ExcludedGameIdsThisSession);
        Assert.All(requests, r => Assert.Same(AdvancedFilters.None, r.Advanced));
        await settings.DidNotReceive().UpsertAsync(Arg.Any<ProfileSettings>(), Arg.Any<CancellationToken>());
        await settings.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.Same(persisted, await settings.GetAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task Superseded_response_cannot_replace_the_new_selection_even_if_provider_ignores_cancellation()
    {
        var oldResult = new TaskCompletionSource<RecommendationResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.GetRecommendationAsync(Arg.Any<RecommendationRequest>(), snapshot, Arg.Any<CancellationToken>())
            .Returns(oldResult.Task, Task.FromResult<RecommendationResult?>(Result(9, 900)));
        using var model = Create();
        var opening = model.OpenAsync(7);
        model.Mood = MoodFilter.LowEnergy;
        await model.RefreshCommand.ExecutionTask!;
        oldResult.SetResult(Result(4, 400));
        await opening;
        Assert.Equal(9, model.Recommendation!.Game.GameId);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Artwork_failure_does_not_block_play_and_launch_failure_keeps_the_window_open()
    {
        artwork.FetchHeaderImageAsync(123, Arg.Any<CancellationToken>()).Returns(Task.FromException<string?>(new IOException()));
        launcher.LaunchAsync(Arg.Any<Uri>()).Returns(false);
        using var model = Create();
        var closed = false;
        model.Played += (_, _) => closed = true;
        await model.OpenAsync(7);
        Assert.NotNull(model.Recommendation);
        Assert.True(model.PlayCommand.CanExecute(null));
        await model.PlayCommand.ExecuteAsync(null);
        Assert.False(closed);
        Assert.Contains("Steam could not", model.Status);
    }

    [Fact]
    public async Task Missing_profile_and_empty_pool_never_launch_a_game()
    {
        using var model = Create();
        await model.OpenAsync(null);
        Assert.False(model.PlayCommand.CanExecute(null));
        Assert.Empty(snapshots.ReceivedCalls());
        engine.GetRecommendationAsync(Arg.Any<RecommendationRequest>(), snapshot, Arg.Any<CancellationToken>())
            .Returns((RecommendationResult?)null);
        await model.OpenAsync(7);
        Assert.Null(model.Recommendation);
        Assert.False(model.PlayCommand.CanExecute(null));
        Assert.True(model.RerollCommand.CanExecute(null));
    }
}

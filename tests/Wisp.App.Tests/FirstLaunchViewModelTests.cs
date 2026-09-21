using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wisp.App.ViewModels;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class FirstLaunchViewModelTests
{
    private readonly ISteamLocator locator = Substitute.For<ISteamLocator>();
    private readonly ISteamProfileRepository profiles = Substitute.For<ISteamProfileRepository>();
    private readonly IGameRepository games = Substitute.For<IGameRepository>();

    private FirstLaunchViewModel Create(bool steamFound = true, bool profileFound = true)
    {
        locator.TryGetSteamInstallPath(out Arg.Any<string>()).Returns(call => { call[0] = "Steam"; return steamFound; });
        profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns(profileFound ? new SteamProfile[] { new() { ProfileId = 7 } } : []);
        return new(locator, profiles, games, NullLogger<FirstLaunchViewModel>.Instance);
    }

    [Fact]
    public async Task WaitsForStartupThenCountsInstalledGamesAndStartsOnlyOnceWithoutWriting()
    {
        using var model = Create();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        games.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new Game[]
        {
            new() { Installed = true }, new() { Installed = false }, new() { Installed = true }
        });
        var started = 0;
        model.Started += (_, _) => started++;
        var loading = model.OpenAsync(ready.Task);
        Assert.False(model.GetStartedCommand.CanExecute(null));
        Assert.Empty(games.ReceivedCalls());
        Assert.Empty(profiles.ReceivedCalls());
        ready.SetResult();
        await loading;
        Assert.Equal("Steam found", model.Status);
        Assert.Equal("2 installed games", model.InstalledGames);
        Assert.True(model.GetStartedCommand.CanExecute(null));
        model.GetStartedCommand.Execute(null);
        model.GetStartedCommand.Execute(null);
        Assert.Equal(1, started);
        Assert.False(model.CanStart);
        Assert.Equal(nameof(IGameRepository.GetAllAsync), Assert.Single(games.ReceivedCalls()).GetMethodInfo().Name);
        Assert.Equal(nameof(ISteamProfileRepository.GetAllAsync), Assert.Single(profiles.ReceivedCalls()).GetMethodInfo().Name);
    }

    [Fact]
    public async Task EmptyLibraryStillAllowsGetStarted()
    {
        using var model = Create();
        games.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Game>());
        await model.OpenAsync(Task.CompletedTask);
        Assert.Equal("0 installed games", model.InstalledGames);
        Assert.True(model.CanStart);
    }

    [Theory]
    [InlineData(false, false, "Steam not found")]
    [InlineData(true, false, "Steam found")]
    public async Task MissingSteamOrProfileLeavesStartDisabled(bool steamFound, bool profileFound, string status)
    {
        using var model = Create(steamFound, profileFound);
        await model.OpenAsync(Task.CompletedTask);
        Assert.Equal(status, model.Status);
        Assert.Contains("restart Wisp", model.InstalledGames);
        Assert.False(model.GetStartedCommand.CanExecute(null));
        Assert.Empty(games.ReceivedCalls());
    }

    [Fact]
    public async Task StartupFailureDoesNotPresentASuccessfulCount()
    {
        using var model = Create();
        await model.OpenAsync(Task.FromException(new IOException("Startup failed")));
        Assert.Equal("Could not load Steam games", model.Status);
        Assert.False(model.CanStart);
        Assert.Empty(games.ReceivedCalls());
    }

    [Fact]
    public async Task RepositoryFailureDisablesStart()
    {
        using var model = Create();
        games.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<Game>>(new IOException()));
        await model.OpenAsync(Task.CompletedTask);
        Assert.Equal("Could not load Steam games", model.Status);
        Assert.False(model.CanStart);
    }

    [Fact]
    public async Task ClosingDuringStartupCancelsWaitingWithoutLoadingRepositories()
    {
        var model = Create();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = model.OpenAsync(ready.Task);
        model.Dispose();
        await loading;
        ready.SetResult();
        Assert.False(model.CanStart);
        Assert.Empty(games.ReceivedCalls());
        Assert.Empty(profiles.ReceivedCalls());
    }

    [Fact]
    public async Task ClosingDuringReadIgnoresLateResult()
    {
        var model = Create();
        var read = new TaskCompletionSource<IReadOnlyList<Game>>(TaskCreationOptions.RunContinuationsAsynchronously);
        games.GetAllAsync(Arg.Any<CancellationToken>()).Returns(read.Task);
        var loading = model.OpenAsync(Task.CompletedTask);
        model.Dispose();
        read.SetResult([new Game { Installed = true }]);
        await loading;
        Assert.False(model.CanStart);
        Assert.Empty(model.InstalledGames);
    }
}

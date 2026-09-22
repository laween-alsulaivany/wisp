using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wisp.App.ViewModels;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class SettingsViewModelTests
{
    private readonly ISettingsRepository settings = Substitute.For<ISettingsRepository>();
    private readonly IStartupRegistrar startup = Substitute.For<IStartupRegistrar>();
    private readonly IGameStateService states = Substitute.For<IGameStateService>();
    private readonly SettingsViewModel model;
    private bool registered;

    public SettingsViewModelTests()
    {
        startup.IsRegistered().Returns(_ => registered);
        startup.When(s => s.Register(Arg.Any<string>())).Do(_ => registered = true);
        startup.When(s => s.Unregister()).Do(_ => registered = false);
        settings.GetAsync(7, default).Returns(new ProfileSettings { ProfileId = 7, StartWithWindows = false });
        model = new(settings, startup, states, NullLogger<SettingsViewModel>.Instance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoadUsesActualRegistrationAndReconcilesStoredFlagWithoutChangingRegistry(bool actual)
    {
        registered = actual;
        settings.GetAsync(7, default).Returns(new ProfileSettings { ProfileId = 7, StartWithWindows = !actual });
        await model.OpenAsync(7);
        Assert.Equal(actual, model.StartWithWindows);
        startup.DidNotReceive().Register(Arg.Any<string>());
        startup.DidNotReceive().Unregister();
        await settings.Received(1).UpsertAsync(Arg.Is<ProfileSettings>(s => s.ProfileId == 7 && s.StartWithWindows == actual), default);
    }

    [Fact]
    public async Task ToggleRegistersRunningExecutableAndUnregistersAndPersistsBothValues()
    {
        await model.OpenAsync(7);
        await model.SetStartupCommand.ExecuteAsync(true);
        startup.Received(1).Register(Environment.ProcessPath!);
        Assert.True(model.StartWithWindows);
        await settings.Received(1).UpsertAsync(Arg.Is<ProfileSettings>(s => s.ProfileId == 7 && s.StartWithWindows), default);
        await model.SetStartupCommand.ExecuteAsync(false);
        startup.Received(1).Unregister();
        Assert.False(model.StartWithWindows);
        await settings.Received(1).UpsertAsync(Arg.Is<ProfileSettings>(s => s.ProfileId == 7 && !s.StartWithWindows), default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPersistenceRestoresPreviousRegistrationAndDisplayedValue(bool previous)
    {
        registered = previous;
        settings.GetAsync(7, default).Returns(new ProfileSettings { ProfileId = 7, StartWithWindows = previous });
        await model.OpenAsync(7);
        settings.UpsertAsync(Arg.Any<ProfileSettings>(), default).Returns(Task.FromException(new IOException("disk unavailable")));
        await model.SetStartupCommand.ExecuteAsync(!previous);
        Assert.Equal(previous, registered);
        Assert.Equal(previous, model.StartWithWindows);
        Assert.Contains("Could not change", model.Status);
        Assert.True(model.CanEdit);
    }

    [Fact]
    public async Task RegistryFailureDoesNotPersistAnIncorrectFlag()
    {
        await model.OpenAsync(7);
        startup.When(s => s.Register(Arg.Any<string>())).Do(_ => throw new UnauthorizedAccessException());
        await model.SetStartupCommand.ExecuteAsync(true);
        Assert.False(model.StartWithWindows);
        await settings.DidNotReceive().UpsertAsync(Arg.Any<ProfileSettings>(), default);
        Assert.Contains("Could not change", model.Status);
    }

    [Fact]
    public async Task ResetCallsOnlyTheServiceWithTheCurrentProfile()
    {
        await model.OpenAsync(7);
        var notified = false;
        model.HistoryReset += (_, _) => notified = true;
        settings.ClearReceivedCalls();
        startup.ClearReceivedCalls();
        await model.ResetHistoryCommand.ExecuteAsync(null);
        await states.Received(1).ResetRecommendationHistoryAsync(7, default);
        Assert.Single(states.ReceivedCalls());
        Assert.Empty(settings.ReceivedCalls());
        Assert.Empty(startup.ReceivedCalls());
        Assert.True(notified);
    }

    [Fact]
    public async Task ResetIsDisabledForActiveSessionsAndReenabledAfterTheyEnd()
    {
        await model.OpenAsync(7);
        Assert.True(model.ResetHistoryCommand.CanExecute(null));
        model.HasActiveSession = true;
        Assert.False(model.CanResetHistory);
        Assert.False(model.ResetHistoryCommand.CanExecute(null));
        Assert.NotEmpty(model.ResetAvailability);
        await model.ResetHistoryCommand.ExecuteAsync(null);
        Assert.Empty(states.ReceivedCalls());
        model.HasActiveSession = false;
        Assert.True(model.ResetHistoryCommand.CanExecute(null));
        Assert.Empty(states.ReceivedCalls());
    }

    [Fact]
    public async Task SavesOnlySelectedProfileAndPreservesUnexposedSetting()
    {
        settings.GetAsync(7, default).Returns(new ProfileSettings { ProfileId = 7, StartWithWindows = false, AdvancedFiltersExpanded = true });
        await model.OpenAsync(7);
        model.MaybeLaterCooldownDays = 19;
        model.IncludeFinishedGames = true;
        model.IncludeDemos = true;
        model.IncludeVrOnly = true;
        model.ShowPostSessionFeedback = false;
        await model.SaveCommand.ExecuteAsync(null);
        await settings.Received(1).UpsertAsync(Arg.Is<ProfileSettings>(s => s.ProfileId == 7 &&
            s.MaybeLaterCooldownDays == 19 && s.IncludeFinishedGames && s.IncludeDemos && s.IncludeVrOnly &&
            !s.ShowPostSessionFeedback && !s.StartWithWindows && s.AdvancedFiltersExpanded), default);
        startup.DidNotReceive().Register(Arg.Any<string>());
        startup.DidNotReceive().Unregister();
        model.MaybeLaterCooldownDays = -1;
        Assert.False(model.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task NoProfileDisablesAllMutationsWithoutRepositoryCalls()
    {
        await model.OpenAsync(null);
        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.False(model.SetStartupCommand.CanExecute(true));
        Assert.False(model.ResetHistoryCommand.CanExecute(null));
        Assert.Empty(settings.ReceivedCalls());
        Assert.Empty(startup.ReceivedCalls());
        Assert.Empty(states.ReceivedCalls());
    }

    [Fact]
    public async Task ResetFailureKeepsScreenAvailableAndDoesNotAnnounceSuccess()
    {
        await model.OpenAsync(7);
        states.ResetRecommendationHistoryAsync(7, default).Returns(Task.FromException(new IOException()));
        var notified = false;
        model.HistoryReset += (_, _) => notified = true;
        await model.ResetHistoryCommand.ExecuteAsync(null);
        Assert.False(notified);
        Assert.Contains("Could not reset", model.Status);
        Assert.True(model.CanResetHistory);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wisp.App.ViewModels;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class LibraryViewModelTests
{
    [Theory]
    [InlineData(GameStateKind.NoData)]
    [InlineData(GameStateKind.Active)]
    [InlineData(GameStateKind.MaybeLater)]
    [InlineData(GameStateKind.Finished)]
    [InlineData(GameStateKind.Dropped)]
    public async Task ManualCorrectionUsesExactSelectedGameProfileAndState(GameStateKind target)
    {
        var games = Substitute.For<IGameRepository>();
        var states = Substitute.For<IGameStateRepository>();
        var service = Substitute.For<IGameStateService>();
        games.GetAllAsync(default).Returns(new Game[]
        {
            new() { GameId = 42, Name = "Beta", Installed = true }, new() { GameId = 91, Name = "Alpha" }
        });
        var model = new LibraryViewModel(games, states, service, NullLogger<LibraryViewModel>.Instance);
        await model.OpenAsync(7);
        Assert.Equal(new[] { 91, 42 }, model.Entries.Select(e => e.Game.GameId));
        Assert.Equal(GameStateKind.NoData, model.Entries[0].State.State);
        Assert.Contains("Normal", model.Entries[0].Display);
        model.Selected = model.Entries[1];
        model.TargetState = target;
        await model.RestoreCommand.ExecuteAsync(null);
        await service.Received(1).RestoreAsync(42, 7, target, default);
        Assert.Single(service.ReceivedCalls());
        await states.DidNotReceive().UpsertAsync(Arg.Any<GameState>(), default);
        await games.DidNotReceive().UpsertAsync(Arg.Any<Game>(), default);
    }

    [Fact]
    public async Task LoadFailureDisablesCorrectionAndReportsError()
    {
        var games = Substitute.For<IGameRepository>();
        games.GetAllAsync(default).Returns(Task.FromException<IReadOnlyList<Game>>(new IOException()));
        var model = new LibraryViewModel(games, Substitute.For<IGameStateRepository>(),
            Substitute.For<IGameStateService>(), NullLogger<LibraryViewModel>.Instance);
        await model.OpenAsync(7);
        Assert.False(model.CanEdit);
        Assert.False(model.RestoreCommand.CanExecute(null));
        Assert.Contains("Could not load", model.Status);
    }
}

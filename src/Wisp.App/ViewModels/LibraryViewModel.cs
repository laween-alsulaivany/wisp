using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;

namespace Wisp.App.ViewModels;

public sealed record StateOption(string Label, GameStateKind State);
public sealed record LibraryEntry(Game Game, GameState State)
{
    public string Display => $"{Game.Name} · {(Game.Installed ? "Installed" : "Not installed")} · " +
        $"{LibraryViewModel.StateOptions.Single(o => o.State == State.State).Label}" +
        (State.State == GameStateKind.Active ? $" · Rank {State.ActiveRankScore:0.##}" : string.Empty) +
        (State.MaybeLaterUntilUtc is { } until ? $" · Cooldown until {until.ToLocalTime():g}" : string.Empty);
}

public sealed class LibraryViewModel : ProfileScreenViewModel
{
    private readonly IGameRepository games;
    private readonly IGameStateRepository states;
    private readonly IGameStateService service;
    private IReadOnlyList<LibraryEntry> entries = [];
    private LibraryEntry? selected;
    private GameStateKind targetState;
    public static IReadOnlyList<StateOption> StateOptions { get; } =
        [new("Normal", GameStateKind.NoData), new("Active", GameStateKind.Active),
         new("Maybe Later", GameStateKind.MaybeLater), new("Finished", GameStateKind.Finished), new("Dropped", GameStateKind.Dropped)];

    public LibraryViewModel(IGameRepository games, IGameStateRepository states, IGameStateService service,
        ILogger<LibraryViewModel> logger) : base(logger)
    {
        this.games = games;
        this.states = states;
        this.service = service;
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => CanEdit && Selected is not null);
    }

    public IReadOnlyList<LibraryEntry> Entries { get => entries; private set => SetProperty(ref entries, value); }
    public LibraryEntry? Selected
    {
        get => selected;
        set
        {
            if (!SetProperty(ref selected, value)) return;
            if (value is not null) TargetState = value.State.State;
            RestoreCommand.NotifyCanExecuteChanged();
        }
    }
    public GameStateKind TargetState { get => targetState; set => SetProperty(ref targetState, value); }
    public IAsyncRelayCommand RestoreCommand { get; }

    protected override async Task LoadAsync(int profileId)
    {
        Selected = null;
        var rows = new List<LibraryEntry>();
        foreach (var game in (await games.GetAllAsync(default)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            rows.Add(new(game, await states.GetAsync(game.GameId, profileId, default)
                ?? new GameState { GameId = game.GameId, ProfileId = profileId }));
        Entries = rows;
        if (rows.Count == 0) Status = "No games found in the local library.";
    }

    private Task RestoreAsync() => RunAsync(async () =>
    {
        var row = Selected!;
        await service.RestoreAsync(row.Game.GameId, ProfileId!.Value, TargetState, default);
        await LoadAsync(ProfileId.Value);
        Selected = Entries.Single(e => e.Game.GameId == row.Game.GameId);
        Status = "Game state updated.";
    }, "Could not update the game state. Try again.");

    protected override void NotifyCommands() => RestoreCommand.NotifyCanExecuteChanged();
}

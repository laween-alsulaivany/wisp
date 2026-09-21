using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.App.ViewModels;

public sealed class SettingsViewModel : ProfileScreenViewModel
{
    private readonly ISettingsRepository settings;
    private readonly IStartupRegistrar startup;
    private readonly IGameStateService states;
    private ProfileSettings saved = new();
    private ProfileSettings draft = new();
    private bool startWithWindows;
    private bool hasActiveSession;

    public SettingsViewModel(ISettingsRepository settings, IStartupRegistrar startup, IGameStateService states,
        ILogger<SettingsViewModel> logger) : base(logger)
    {
        this.settings = settings;
        this.startup = startup;
        this.states = states;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit && MaybeLaterCooldownDays >= 0);
        SetStartupCommand = new AsyncRelayCommand<bool>(SetStartupAsync, _ => CanEdit);
        ResetHistoryCommand = new AsyncRelayCommand(ResetAsync, () => CanResetHistory);
    }

    public int MaybeLaterCooldownDays
    {
        get => draft.MaybeLaterCooldownDays;
        set { SetProperty(ref draft, draft with { MaybeLaterCooldownDays = value }); SaveCommand.NotifyCanExecuteChanged(); }
    }
    public bool IncludeFinishedGames { get => draft.IncludeFinishedGames; set => SetProperty(ref draft, draft with { IncludeFinishedGames = value }); }
    public bool IncludeDemos { get => draft.IncludeDemos; set => SetProperty(ref draft, draft with { IncludeDemos = value }); }
    public bool IncludeVrOnly { get => draft.IncludeVrOnly; set => SetProperty(ref draft, draft with { IncludeVrOnly = value }); }
    public bool ShowPostSessionFeedback { get => draft.ShowPostSessionFeedback; set => SetProperty(ref draft, draft with { ShowPostSessionFeedback = value }); }
    public bool StartWithWindows { get => startWithWindows; private set => SetProperty(ref startWithWindows, value); }
    public bool HasActiveSession
    {
        get => hasActiveSession;
        set
        {
            if (!SetProperty(ref hasActiveSession, value)) return;
            OnPropertyChanged(nameof(CanResetHistory));
            OnPropertyChanged(nameof(ResetAvailability));
            ResetHistoryCommand.NotifyCanExecuteChanged();
        }
    }
    public bool CanResetHistory => CanEdit && !HasActiveSession;
    public string ResetAvailability => HasActiveSession ? "Finish the active game session before resetting history." : string.Empty;
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand<bool> SetStartupCommand { get; }
    public IAsyncRelayCommand ResetHistoryCommand { get; }
    public event EventHandler? HistoryReset;

    private Task ResetAsync()
    {
        if (!CanResetHistory) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await states.ResetRecommendationHistoryAsync(ProfileId!.Value, default);
            HistoryReset?.Invoke(this, EventArgs.Empty);
            Status = "Recommendation history reset.";
        }, "Could not reset recommendation history. Try again.");
    }

    protected override async Task LoadAsync(int profileId)
    {
        saved = await settings.GetAsync(profileId, default) ?? new ProfileSettings { ProfileId = profileId };
        StartWithWindows = startup.IsRegistered();
        if (saved.StartWithWindows != StartWithWindows)
        {
            saved = saved with { StartWithWindows = StartWithWindows };
            await settings.UpsertAsync(saved, default);
        }
        draft = saved;
        OnPropertyChanged(string.Empty);
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        StartWithWindows = startup.IsRegistered();
        var updated = draft with { StartWithWindows = StartWithWindows };
        await settings.UpsertAsync(updated, default);
        saved = draft = updated;
        Status = "Settings saved.";
    }, "Could not save settings. Try again.");

    private Task SetStartupAsync(bool enabled) => RunAsync(async () =>
    {
        var previous = startup.IsRegistered();
        try
        {
            if (previous != enabled) SetRegistration(enabled);
            var actual = startup.IsRegistered();
            if (actual != enabled) throw new InvalidOperationException("Startup registration did not change.");
            var updated = saved with { StartWithWindows = actual };
            await settings.UpsertAsync(updated, default);
            saved = updated;
            draft = draft with { StartWithWindows = actual };
        }
        catch
        {
            if (startup.IsRegistered() != previous) SetRegistration(previous);
            throw;
        }
        finally { StartWithWindows = startup.IsRegistered(); }
    }, "Could not change Start with Windows. Try again.");

    private void SetRegistration(bool enabled)
    {
        if (enabled) startup.Register(Environment.ProcessPath
            ?? throw new InvalidOperationException("The running executable path is unavailable."));
        else startup.Unregister();
    }

    protected override void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        SetStartupCommand.NotifyCanExecuteChanged();
        ResetHistoryCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanResetHistory));
    }
}

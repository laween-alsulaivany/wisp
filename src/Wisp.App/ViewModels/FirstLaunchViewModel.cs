using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wisp.Core.Interfaces;

namespace Wisp.App.ViewModels;

public sealed class FirstLaunchViewModel : ObservableObject, IDisposable
{
    private readonly ISteamLocator locator;
    private readonly ISteamProfileRepository profiles;
    private readonly IGameRepository games;
    private readonly ILogger<FirstLaunchViewModel> logger;
    private readonly CancellationTokenSource lifetime = new();
    private string status = "Looking for Steam…";
    private string installedGames = string.Empty;
    private bool canStart;
    private bool disposed;

    public FirstLaunchViewModel(ISteamLocator locator, ISteamProfileRepository profiles,
        IGameRepository games, ILogger<FirstLaunchViewModel> logger)
    {
        this.locator = locator;
        this.profiles = profiles;
        this.games = games;
        this.logger = logger;
        GetStartedCommand = new RelayCommand(() =>
        {
            if (!CanStart) return;
            CanStart = false;
            Started?.Invoke(this, EventArgs.Empty);
        }, () => CanStart);
    }

    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string InstalledGames { get => installedGames; private set => SetProperty(ref installedGames, value); }
    public bool CanStart
    {
        get => canStart;
        private set { if (SetProperty(ref canStart, value)) GetStartedCommand.NotifyCanExecuteChanged(); }
    }
    public IRelayCommand GetStartedCommand { get; }
    public event EventHandler? Started;

    public async Task OpenAsync(Task startupCompleted)
    {
        try
        {
            await startupCompleted.WaitAsync(lifetime.Token);
            if (!locator.TryGetSteamInstallPath(out _))
            {
                Status = "Steam not found";
                InstalledGames = "Open Steam, then restart Wisp.";
                return;
            }
            if ((await profiles.GetAllAsync(lifetime.Token)).Count == 0)
            {
                if (disposed) return;
                Status = "Steam found";
                InstalledGames = "Sign in to Steam, then restart Wisp.";
                return;
            }
            var installed = await games.GetAllAsync(lifetime.Token);
            if (disposed) return;
            Status = "Steam found";
            InstalledGames = $"{installed.Count(game => game.Installed)} installed games";
            CanStart = true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not load first-launch screen");
            if (disposed) return;
            Status = "Could not load Steam games";
            InstalledGames = "Restart Wisp to try again.";
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CanStart = false;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}

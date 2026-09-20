using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;

namespace Wisp.App.ViewModels;

public sealed class RecommendationViewModel : ObservableObject, IDisposable
{
    private readonly IRecommendationEngine engine;
    private readonly IRecommendationSnapshotProvider snapshots;
    private readonly ISessionTracker sessions;
    private readonly IArtworkFetcher artwork;
    private readonly IUriLauncher launcher;
    private readonly ILogger<RecommendationViewModel> logger;
    private readonly HashSet<int> excluded = [];
    private CancellationTokenSource? loading;
    private int? profileId;
    private MoodFilter mood;
    private TimeFilter time;
    private RecommendationResult? recommendation;
    private string? artworkPath;
    private string status = string.Empty;
    private bool isBusy;
    private bool disposed;

    public RecommendationViewModel(IRecommendationEngine engine, IRecommendationSnapshotProvider snapshots,
        ISessionTracker sessions, IArtworkFetcher artwork, IUriLauncher launcher,
        ILogger<RecommendationViewModel> logger)
    {
        this.engine = engine;
        this.snapshots = snapshots;
        this.sessions = sessions;
        this.artwork = artwork;
        this.launcher = launcher;
        this.logger = logger;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        RerollCommand = new AsyncRelayCommand(RerollAsync, () => profileId.HasValue && !IsBusy && !disposed);
        PlayCommand = new AsyncRelayCommand(PlayAsync, () => Recommendation is not null && !IsBusy && !disposed);
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RerollCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }
    public event EventHandler? Played;
    public MoodFilter Mood
    {
        get => mood;
        set { if (SetProperty(ref mood, value)) RefreshCommand.Execute(null); }
    }
    public TimeFilter Time
    {
        get => time;
        set { if (SetProperty(ref time, value)) RefreshCommand.Execute(null); }
    }
    public RecommendationResult? Recommendation
    {
        get => recommendation;
        private set { SetProperty(ref recommendation, value); PlayCommand.NotifyCanExecuteChanged(); }
    }
    public string? ArtworkPath { get => artworkPath; private set => SetProperty(ref artworkPath, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            SetProperty(ref isBusy, value);
            RerollCommand.NotifyCanExecuteChanged();
            PlayCommand.NotifyCanExecuteChanged();
        }
    }

    public Task OpenAsync(int? activeProfileId)
    {
        profileId = activeProfileId;
        excluded.Clear();
        mood = MoodFilter.Anything;
        time = default;
        OnPropertyChanged(nameof(Mood));
        OnPropertyChanged(nameof(Time));
        return RefreshAsync();
    }

    private Task RerollAsync()
    {
        if (Recommendation is { } current) excluded.Add(current.Game.GameId);
        return RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (disposed) return;
        loading?.Cancel();
        using var requestLifetime = new CancellationTokenSource();
        loading = requestLifetime;
        var ct = requestLifetime.Token;
        Recommendation = null;
        ArtworkPath = null;
        Status = string.Empty;
        IsBusy = true;
        try
        {
            if (profileId is not { } id)
            {
                Status = "Open Steam with a local profile to pick a game.";
                return;
            }
            var request = new RecommendationRequest
            {
                ProfileId = id, Mood = Mood, Time = Time, Advanced = AdvancedFilters.None,
                ExcludedGameIdsThisSession = new HashSet<int>(excluded)
            };
            var snapshot = await snapshots.BuildSnapshotAsync(id, ct);
            ct.ThrowIfCancellationRequested();
            var result = await engine.GetRecommendationAsync(request, snapshot, ct);
            ct.ThrowIfCancellationRequested();
            Recommendation = result;
            if (result is null)
            {
                Status = "No eligible games are available.";
                return;
            }
            // Artwork is optional and must never prevent playing the recommendation.
            IsBusy = false;
            try
            {
                var path = result.Game.HeaderImagePath ?? await artwork.FetchHeaderImageAsync(result.Game.AppId, ct);
                ct.ThrowIfCancellationRequested();
                ArtworkPath = path;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not load recommendation artwork");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!ct.IsCancellationRequested)
            {
                Status = "Could not load a recommendation. Try again.";
                logger.LogError(exception, "Could not load recommendation");
            }
        }
        finally
        {
            if (ReferenceEquals(loading, requestLifetime))
            {
                loading = null;
                IsBusy = false;
            }
        }
    }

    private async Task PlayAsync()
    {
        if (Recommendation is not { } current || disposed) return;
        try
        {
            var uri = new Uri($"steam://run/{current.Game.AppId}");
            sessions.NotifyRecommendationLaunch(current.Game.AppId);
            if (await launcher.LaunchAsync(uri)) Played?.Invoke(this, EventArgs.Empty);
            else Status = "Steam could not open this game. Try again.";
        }
        catch (Exception exception)
        {
            Status = "Steam could not open this game. Try again.";
            logger.LogError(exception, "Could not launch recommendation");
        }
    }

    public void Dispose()
    {
        disposed = true;
        loading?.Cancel();
        PlayCommand.NotifyCanExecuteChanged();
        RerollCommand.NotifyCanExecuteChanged();
    }
}

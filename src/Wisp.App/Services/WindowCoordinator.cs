using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Wisp.App.Services.Hosting;
using Wisp.App.ViewModels;
using Wisp.App.Views;
using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;

namespace Wisp.App.Services;

public sealed class WindowCoordinator : IDisposable
{
    private readonly IServiceProvider services;
    private readonly SessionTrackingService sessions;
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly ILogger<WindowCoordinator> logger;
    private readonly Dictionary<int, (FeedbackWindow Window, FeedbackViewModel Model)> feedbackWindows = [];
    private readonly HashSet<int> presentedSessions = [];
    private RecommendationWindow? recommendationWindow;
    private SteamButtonWindow? steamButton;
    private bool disposed;

    public WindowCoordinator(IServiceProvider services)
    {
        this.services = services;
        sessions = services.GetRequiredService<SessionTrackingService>();
        logger = services.GetRequiredService<ILogger<WindowCoordinator>>();
        sessions.ActiveProfileChanged += OnProfileChanged;
        sessions.SessionEnded += OnSessionEnded;
        SteamButtonViewModel? buttonModel = null;
        try
        {
            buttonModel = new SteamButtonViewModel(services.GetRequiredService<ISteamWindowTracker>(),
                action => dispatcher.TryEnqueue(() => { if (!disposed) action(); }), OpenRecommendation);
            steamButton = new SteamButtonWindow(buttonModel, logger);
        }
        catch (Exception exception)
        {
            buttonModel?.Dispose();
            logger.LogWarning(exception, "Steam button unavailable; tray and hotkey remain available");
        }
    }

    public void OpenRecommendation()
    {
        if (disposed) return;
        if (recommendationWindow is not null) { recommendationWindow.Activate(); return; }
        var scope = services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<RecommendationViewModel>();
        var window = new RecommendationWindow(model);
        recommendationWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(recommendationWindow, window)) recommendationWindow = null;
            scope.Dispose();
        };
        window.Activate();
        _ = model.OpenAsync(sessions.ActiveProfileId);
    }

    private void OnProfileChanged(object? sender, EventArgs args) => dispatcher.TryEnqueue(() =>
    {
        if (disposed) return;
        recommendationWindow?.Close();
        _ = RefreshPendingAsync();
    });

    private void OnSessionEnded(object? sender, SessionEndedEventArgs args) =>
        dispatcher.TryEnqueue(() => { if (!disposed) _ = ShowFeedbackAsync(args); });

    private async Task ShowFeedbackAsync(SessionEndedEventArgs args)
    {
        var session = args.Session;
        if (!presentedSessions.Add(session.SessionId)) return;
        var model = new FeedbackViewModel(session.SessionId, TimeSpan.FromSeconds(session.RuntimeSeconds ?? 0),
            services.GetRequiredService<IGameStateService>(), services.GetRequiredService<ILogger<FeedbackViewModel>>());
        if (!model.ShouldShow) return;
        try
        {
            var settings = await services.GetRequiredService<ISettingsRepository>().GetAsync(session.ProfileId, CancellationToken.None);
            if (disposed || settings?.ShowPostSessionFeedback == false) return;
            // A previous profile's final session remains answerable in History without interrupting the new profile.
            if (sessions.ActiveProfileId != session.ProfileId)
            {
                await model.DismissCommand.ExecuteAsync(null);
                return;
            }
            var window = new FeedbackWindow(model);
            feedbackWindows.Add(session.SessionId, (window, model));
            window.Closed += (_, _) => { feedbackWindows.Remove(session.SessionId); _ = RefreshPendingAsync(); };
            window.AppWindow.Show(activateWindow: false);
        }
        catch (Exception exception) { logger.LogError(exception, "Could not show session feedback"); }
    }

    private async Task RefreshPendingAsync()
    {
        if (disposed) return;
        var profileId = sessions.ActiveProfileId;
        try
        {
            var pending = profileId is { } id
                ? await services.GetRequiredService<IFeedbackRepository>().GetPendingAsync(id, CancellationToken.None) : [];
            if (!disposed && sessions.ActiveProfileId == profileId) steamButton?.SetPending(pending.Count > 0);
        }
        catch (Exception exception) { logger.LogWarning(exception, "Could not refresh pending feedback badge"); }
    }

    public async Task ShutdownAsync()
    {
        disposed = true;
        sessions.ActiveProfileChanged -= OnProfileChanged;
        sessions.SessionEnded -= OnSessionEnded;
        foreach (var (window, model) in feedbackWindows.Values.ToArray())
        {
            if (model.SubmitCommand.ExecutionTask is { } submitting) await submitting;
            if (model.DismissCommand.ExecutionTask is { } dismissing) await dismissing;
            if (!model.IsCompleted) await model.DismissCommand.ExecuteAsync(null);
            window.Close();
        }
    }

    public void Dispose()
    {
        disposed = true;
        sessions.ActiveProfileChanged -= OnProfileChanged;
        sessions.SessionEnded -= OnSessionEnded;
        recommendationWindow?.Close();
        steamButton?.Close();
    }
}

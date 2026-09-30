using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;

namespace Wisp.App.ViewModels;

public sealed record HistoryEntry(Session Session, string GameName, Feedback? Feedback)
{
    public string Display => $"{GameName} · {Session.StartUtc.ToLocalTime():g}\n" +
        $"Runtime: {(Session.RuntimeSeconds is { } runtime ? TimeSpan.FromSeconds(runtime).ToString() : "In progress")} · " +
        $"Active time: {TimeSpan.FromSeconds(Session.ActiveForegroundSeconds)} · " +
        $"Feedback: {(Feedback is null ? "None" : HistoryViewModel.FeedbackOptions.Single(o => o.Type == Feedback.FeedbackType).Label)}";
}

public sealed class HistoryViewModel : ProfileScreenViewModel
{
    private readonly ISessionRepository sessions;
    private readonly IFeedbackRepository feedback;
    private readonly IGameRepository games;
    private readonly IClock clock;
    private IReadOnlyList<HistoryEntry> entries = [];
    private HistoryEntry? selected;
    private FeedbackType selectedFeedback;
    public static IReadOnlyList<FeedbackOption> FeedbackOptions { get; } =
        [new("Keep going", FeedbackType.KeepGoing), new("Not feeling it", FeedbackType.NotFeelingIt),
         new("Technical issue", FeedbackType.TechnicalIssue), new("Interrupted", FeedbackType.Interrupted),
         new("Maybe Later", FeedbackType.MaybeLater), new("Finished", FeedbackType.Finished),
         new("Dropped", FeedbackType.Dropped), new("Pending", FeedbackType.Pending)];

    public HistoryViewModel(ISessionRepository sessions, IFeedbackRepository feedback, IGameRepository games,
        IClock clock, ILogger<HistoryViewModel> logger) : base(logger)
    {
        this.sessions = sessions;
        this.feedback = feedback;
        this.games = games;
        this.clock = clock;
        SaveFeedbackCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit && Selected?.Feedback is not null);
        RemoveFeedbackCommand = new AsyncRelayCommand(RemoveAsync, () => CanEdit && Selected?.Feedback is not null);
    }

    public IReadOnlyList<HistoryEntry> Entries { get => entries; private set => SetProperty(ref entries, value); }
    public HistoryEntry? Selected
    {
        get => selected;
        set
        {
            if (!SetProperty(ref selected, value)) return;
            SelectedFeedback = value?.Feedback?.FeedbackType ?? FeedbackType.Pending;
            NotifyCommands();
        }
    }
    public FeedbackType SelectedFeedback { get => selectedFeedback; set => SetProperty(ref selectedFeedback, value); }
    public IAsyncRelayCommand SaveFeedbackCommand { get; }
    public IAsyncRelayCommand RemoveFeedbackCommand { get; }

    protected override async Task LoadAsync(int profileId)
    {
        Selected = null;
        var recent = await sessions.GetRecentAsync(profileId, 100, default);
        var answers = (await feedback.GetAllAsync(profileId, default)).ToDictionary(f => f.SessionId);
        var names = (await games.GetAllAsync(default)).ToDictionary(g => g.GameId, g => g.Name);
        // Pending answers remain reachable even if their session is older than the recent list.
        var all = recent.ToDictionary(s => s.SessionId);
        foreach (var answer in answers.Values.Where(f => f.IsPending && !all.ContainsKey(f.SessionId)))
            if (await sessions.GetByIdAsync(answer.SessionId, default) is { } session)
                all.Add(session.SessionId, session);
        Entries = all.Values.OrderByDescending(s => s.StartUtc).ThenByDescending(s => s.SessionId)
            .Select(s => new HistoryEntry(s, names.GetValueOrDefault(s.GameId, "Unknown game"), answers.GetValueOrDefault(s.SessionId))).ToArray();
        if (Entries.Count == 0) Status = "No sessions recorded yet.";
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        var original = Selected!.Feedback!;
        await feedback.RecordAsync(original with
        {
            FeedbackType = SelectedFeedback, IsPending = SelectedFeedback == FeedbackType.Pending,
            RecordedUtc = SelectedFeedback == FeedbackType.Pending ? null : clock.UtcNow
        }, default);
        await ReloadSelectionAsync(original.SessionId);
        Status = "Feedback updated.";
    }, "Could not update feedback. Try again.");

    private Task RemoveAsync() => RunAsync(async () =>
    {
        var original = Selected!.Feedback!;
        await feedback.RemoveAsync(original.FeedbackId, default);
        await ReloadSelectionAsync(original.SessionId);
        Status = "Feedback removed.";
    }, "Could not remove feedback. Try again.");

    private async Task ReloadSelectionAsync(int sessionId)
    {
        await LoadAsync(ProfileId!.Value);
        Selected = Entries.FirstOrDefault(e => e.Session.SessionId == sessionId);
    }

    protected override void NotifyCommands()
    {
        SaveFeedbackCommand.NotifyCanExecuteChanged();
        RemoveFeedbackCommand.NotifyCanExecuteChanged();
    }
}

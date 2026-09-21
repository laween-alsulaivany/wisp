using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;

namespace Wisp.App.ViewModels;

public enum FeedbackVariant { Silent, Quick, Normal }
public sealed record FeedbackOption(string Label, FeedbackType Type);

public sealed class FeedbackViewModel : ObservableObject
{
    private readonly IGameStateService states;
    private readonly ILogger<FeedbackViewModel> logger;
    private bool completed;
    private bool saving;
    private string status = string.Empty;

    public FeedbackViewModel(int sessionId, TimeSpan duration, IGameStateService states,
        ILogger<FeedbackViewModel> logger)
    {
        SessionId = sessionId;
        this.states = states;
        this.logger = logger;
        Variant = duration < TimeSpan.FromMinutes(5) ? FeedbackVariant.Silent
            : duration <= TimeSpan.FromMinutes(15) ? FeedbackVariant.Quick : FeedbackVariant.Normal;
        Options = Variant switch
        {
            FeedbackVariant.Quick => [new("Keep going", FeedbackType.KeepGoing),
                new("Not feeling it", FeedbackType.NotFeelingIt), new("Technical issue", FeedbackType.TechnicalIssue),
                new("Interrupted", FeedbackType.Interrupted)],
            FeedbackVariant.Normal => [new("Keep going", FeedbackType.KeepGoing),
                new("Maybe later", FeedbackType.MaybeLater), new("Finished", FeedbackType.Finished),
                new("Drop", FeedbackType.Dropped)],
            _ => []
        };
        SubmitCommand = new AsyncRelayCommand<FeedbackOption>(
            option => SaveAsync(option!.Type), option => CanSave && option is not null && Options.Contains(option));
        DismissCommand = new AsyncRelayCommand(() => SaveAsync(FeedbackType.Pending), () => CanSave);
    }

    public int SessionId { get; }
    public FeedbackVariant Variant { get; }
    public bool ShouldShow => Variant != FeedbackVariant.Silent;
    public string Prompt => Variant == FeedbackVariant.Quick ? "That was quick! What happened?" : "How did that go?";
    public IReadOnlyList<FeedbackOption> Options { get; }
    public IAsyncRelayCommand<FeedbackOption> SubmitCommand { get; }
    public IAsyncRelayCommand DismissCommand { get; }
    public bool IsCompleted => completed;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public event EventHandler? Completed;
    private bool CanSave => ShouldShow && !completed && !saving;

    public void Invalidate()
    {
        // Reset removed the session; closing its prompt must not create pending feedback again.
        completed = true;
        OnPropertyChanged(nameof(IsCompleted));
        NotifyCommands();
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveAsync(FeedbackType type)
    {
        if (!CanSave) return;
        saving = true;
        NotifyCommands();
        try
        {
            await states.ApplyFeedbackAsync(SessionId, type, CancellationToken.None);
            completed = true;
            OnPropertyChanged(nameof(IsCompleted));
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Status = "Could not save feedback. Try again.";
            logger.LogError(exception, "Could not save session feedback");
        }
        finally { saving = false; NotifyCommands(); }
    }

    private void NotifyCommands()
    {
        SubmitCommand.NotifyCanExecuteChanged();
        DismissCommand.NotifyCanExecuteChanged();
    }
}

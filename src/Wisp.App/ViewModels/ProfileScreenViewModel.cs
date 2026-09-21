using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Wisp.App.ViewModels;

public abstract class ProfileScreenViewModel(ILogger logger) : ObservableObject
{
    private bool isBusy;
    private bool loaded;
    private string status = string.Empty;
    protected int? ProfileId { get; private set; }
    public bool IsBusy { get => isBusy; private set => SetProperty(ref isBusy, value); }
    public bool CanEdit => loaded && ProfileId.HasValue && !IsBusy;
    public string Status { get => status; protected set => SetProperty(ref status, value); }

    public Task OpenAsync(int? profileId) => RunAsync(async () =>
    {
        loaded = false;
        ProfileId = profileId;
        if (profileId is not { } id)
        {
            Status = "Open Steam with a local profile to use this screen.";
            return;
        }
        await LoadAsync(id);
        loaded = true;
    }, "Could not load this screen. Close it and try again.");

    protected abstract Task LoadAsync(int profileId);
    protected abstract void NotifyCommands();

    protected async Task RunAsync(Func<Task> action, string failure)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = string.Empty;
        NotifyAvailability();
        try { await action(); }
        catch (Exception exception)
        {
            Status = failure;
            logger.LogError(exception, "Profile screen operation failed");
        }
        finally { IsBusy = false; NotifyAvailability(); }
    }

    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanEdit));
        NotifyCommands();
    }
}

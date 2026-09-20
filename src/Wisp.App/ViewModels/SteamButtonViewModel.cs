using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;

namespace Wisp.App.ViewModels;

public sealed class SteamButtonViewModel : ObservableObject, IDisposable
{
    private readonly ISteamWindowTracker tracker;
    private readonly Action<Action> dispatch;
    private SteamWindowBoundsChangedEventArgs? bounds;
    private bool focused;
    private bool hovered;
    private bool disabled;
    private bool disposed;

    public SteamButtonViewModel(ISteamWindowTracker tracker, Action<Action> dispatch, Action pick)
    {
        this.tracker = tracker;
        this.dispatch = dispatch;
        PickCommand = new RelayCommand(pick);
        tracker.BoundsChanged += OnBoundsChanged;
        tracker.SteamFocusGained += OnFocusGained;
        tracker.SteamFocusLost += OnFocusLost;
    }

    public IRelayCommand PickCommand { get; }
    public bool IsVisible => !disposed && !disabled && focused && bounds is not null && !tracker.IsBigPictureMode;
    public bool IsHovered { get => hovered; set { if (SetProperty(ref hovered, value)) Refresh(); } }
    public string Label => IsHovered ? "Pick for me" : "W";
    public int Width => Scale(IsHovered ? 144 : 44);
    public int Height => Scale(44);
    public int Left => (bounds?.Right ?? 0) - Width - Scale(12);
    public int Top => (bounds?.Bottom ?? 0) - Height - Scale(12);

    private int Scale(int value) => (int)Math.Round(value * (bounds?.Dpi ?? 96) / 96.0);
    private void OnBoundsChanged(object? sender, SteamWindowBoundsChangedEventArgs value) => dispatch(() =>
    {
        if (disposed) return;
        if (value.Dpi == 0 || value.Right <= value.Left || value.Bottom <= value.Top) disabled = true;
        bounds = value;
        Refresh();
    });
    private void OnFocusGained(object? sender, EventArgs args) => dispatch(() => { focused = true; Refresh(); });
    private void OnFocusLost(object? sender, EventArgs args) => dispatch(() => { focused = false; hovered = false; Refresh(); });
    public void Disable() { disabled = true; Refresh(); }
    private void Refresh() => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        disposed = true;
        tracker.BoundsChanged -= OnBoundsChanged;
        tracker.SteamFocusGained -= OnFocusGained;
        tracker.SteamFocusLost -= OnFocusLost;
    }
}

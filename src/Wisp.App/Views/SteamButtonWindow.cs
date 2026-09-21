using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wisp.App.ViewModels;

namespace Wisp.App.Views;

public sealed class SteamButtonWindow : Window
{
    private readonly SteamButtonViewModel model;
    private readonly ILogger logger;
    private readonly Button button;
    private readonly DispatcherTimer expansionTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch expansionClock = new();
    private double currentWidth;
    private double startingWidth;
    private int targetWidth;
    private int lastHeight;
    private bool shown;
    private bool enabled;
    private readonly TextBlock badge = new()
    {
        Text = "●", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false, Visibility = Visibility.Collapsed
    };

    public SteamButtonWindow(SteamButtonViewModel model, ILogger logger)
    {
        this.model = model;
        this.logger = logger;
        Title = "Wisp";
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        AppWindow.SetPresenter(presenter);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // Clicking the companion must not steal Steam's focus and hide the button before Click.
        var previous = SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x00000080);
        if (previous == 0 && Marshal.GetLastPInvokeError() is not 0 and var error)
            throw new Win32Exception(error);
        button = new Button { Command = model.PickCommand, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Pick for me");
        button.PointerEntered += (_, _) => model.IsHovered = true;
        button.PointerExited += (_, _) => model.IsHovered = false;
        var panel = new Grid();
        panel.Children.Add(button);
        panel.Children.Add(badge);
        Content = panel;
        expansionTimer.Tick += (_, _) => AnimateExpansion();
        model.PropertyChanged += Refresh;
        Closed += (_, _) => { expansionTimer.Stop(); model.PropertyChanged -= Refresh; model.Dispose(); };
    }

    public void SetPending(bool pending)
    {
        badge.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(button, pending ? "Pick for me · Pending feedback" : "Pick for me");
    }

    public void Enable()
    {
        enabled = true;
        Refresh(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void Refresh(object? sender, PropertyChangedEventArgs args)
    {
        try
        {
            button.Content = model.Label;
            if (!enabled || !model.IsVisible)
            {
                expansionTimer.Stop();
                shown = false;
                AppWindow.Hide();
                return;
            }
            if (!shown || lastHeight != model.Height || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            {
                expansionTimer.Stop();
                currentWidth = targetWidth = model.Width;
            }
            else if (targetWidth != model.Width)
            {
                startingWidth = currentWidth;
                targetWidth = model.Width;
                expansionClock.Restart();
                expansionTimer.Start();
            }
            lastHeight = model.Height;
            PositionButton();
            AppWindow.Show(activateWindow: false);
            shown = true;
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    private void AnimateExpansion()
    {
        try
        {
            var progress = Math.Min(1, expansionClock.Elapsed.TotalMilliseconds / 180);
            var eased = 1 - Math.Pow(1 - progress, 3);
            currentWidth = startingWidth + (targetWidth - startingWidth) * eased;
            PositionButton();
            if (progress >= 1) expansionTimer.Stop();
        }
        catch (Exception exception) { Disable(exception); }
    }

    private void PositionButton()
    {
        var width = (int)Math.Round(currentWidth);
        // Keep the right edge attached while the native window expands to the left.
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            model.Left + model.Width - width, model.Top, width, model.Height));
    }

    private void Disable(Exception exception)
    {
        expansionTimer.Stop();
        model.PropertyChanged -= Refresh;
        model.Disable();
        AppWindow.Hide();
        logger.LogWarning(exception, "Steam button disabled; tray and hotkey remain available");
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}

using System.ComponentModel;
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
        model.PropertyChanged += Refresh;
        Closed += (_, _) => { model.PropertyChanged -= Refresh; model.Dispose(); };
    }

    public void SetPending(bool pending)
    {
        badge.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(button, pending ? "Pick for me · Pending feedback" : "Pick for me");
    }

    private void Refresh(object? sender, PropertyChangedEventArgs args)
    {
        try
        {
            button.Content = model.Label;
            if (!model.IsVisible) { AppWindow.Hide(); return; }
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(model.Left, model.Top, model.Width, model.Height));
            AppWindow.Show(activateWindow: false);
        }
        catch (Exception exception)
        {
            model.PropertyChanged -= Refresh;
            model.Disable();
            AppWindow.Hide();
            logger.LogWarning(exception, "Steam button disabled; tray and hotkey remain available");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}

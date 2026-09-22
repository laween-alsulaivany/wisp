using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Wisp.App.ViewModels;

namespace Wisp.App.Views;

public sealed class FirstLaunchWindow : Window
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> Completion => completion.Task;

    public FirstLaunchWindow(FirstLaunchViewModel model)
    {
        Title = "Wisp";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(420, 280));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsMaximizable = false;
        var panel = new StackPanel
        {
            DataContext = model, Padding = new Thickness(32), Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        var status = new TextBlock { FontSize = 24, TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(model.Status)) });
        var count = new TextBlock { TextWrapping = TextWrapping.Wrap };
        count.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(model.InstalledGames)) });
        panel.Children.Add(status);
        panel.Children.Add(count);
        panel.Children.Add(new Button { Content = "Get started", Command = model.GetStartedCommand });
        Content = panel;
        var started = false;
        void OnStarted(object? sender, EventArgs args) { started = true; Close(); }
        model.Started += OnStarted;
        Closed += (_, _) =>
        {
            model.Started -= OnStarted;
            model.Dispose();
            completion.TrySetResult(started);
        };
    }
}

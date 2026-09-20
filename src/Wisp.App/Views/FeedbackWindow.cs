using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Wisp.App.ViewModels;

namespace Wisp.App.Views;

public sealed class FeedbackWindow : Window
{
    private readonly FeedbackViewModel model;
    private bool closing;

    public FeedbackWindow(FeedbackViewModel model)
    {
        this.model = model;
        Title = "Wisp - Session feedback";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(420, 430));
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24), DataContext = model };
        panel.Children.Add(new TextBlock { Text = model.Prompt, FontSize = 24, TextWrapping = TextWrapping.Wrap });
        foreach (var option in model.Options)
            panel.Children.Add(new Button { Content = option.Label, Command = model.SubmitCommand,
                CommandParameter = option, HorizontalAlignment = HorizontalAlignment.Stretch });
        panel.Children.Add(new Button { Content = "Not now", Command = model.DismissCommand });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(model.Status)) });
        panel.Children.Add(status);
        Content = panel;
        model.Completed += OnCompleted;
        AppWindow.Closing += (_, args) =>
        {
            if (closing || model.IsCompleted) return;
            args.Cancel = true;
            if (model.DismissCommand.CanExecute(null)) model.DismissCommand.Execute(null);
        };
        Closed += (_, _) => model.Completed -= OnCompleted;
    }

    private void OnCompleted(object? sender, EventArgs args) { closing = true; Close(); }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Wisp.App.ViewModels;

namespace Wisp.App.Views;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel model)
    {
        Title = "Wisp - Settings";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(660, 780));
        var fields = new StackPanel { Spacing = 16 };
        var cooldown = new NumberBox
        {
            Header = "Maybe Later cooldown (days)", Minimum = 0, Maximum = int.MaxValue,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        ProfileScreenLayout.Bind(cooldown, NumberBox.ValueProperty, nameof(model.MaybeLaterCooldownDays), BindingMode.TwoWay);
        fields.Children.Add(cooldown);
        AddToggle(fields, "Include finished games", nameof(model.IncludeFinishedGames));
        AddToggle(fields, "Include demos/prologues", nameof(model.IncludeDemos));
        AddToggle(fields, "Include VR-only games", nameof(model.IncludeVrOnly));
        var startup = new ToggleSwitch { Header = "Start with Windows" };
        ProfileScreenLayout.Bind(startup, ToggleSwitch.IsOnProperty, nameof(model.StartWithWindows));
        startup.Toggled += async (_, _) =>
        {
            if (!model.SetStartupCommand.CanExecute(startup.IsOn) || startup.IsOn == model.StartWithWindows) return;
            await model.SetStartupCommand.ExecuteAsync(startup.IsOn);
            startup.IsOn = model.StartWithWindows;
        };
        fields.Children.Add(startup);
        AddToggle(fields, "Show post-session feedback", nameof(model.ShowPostSessionFeedback));
        fields.Children.Add(new Button { Content = "Save settings", Command = model.SaveCommand });
        var actions = new StackPanel { Spacing = 8 };
        var reset = new Button { Content = "Reset recommendation history" };
        ProfileScreenLayout.Bind(reset, Control.IsEnabledProperty, nameof(model.CanResetHistory));
        reset.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = actions.XamlRoot, Title = "Reset recommendation history?",
                Content = "This clears this profile's sessions, feedback, game states, ranks, and streaks. Games and settings are kept.",
                PrimaryButtonText = "Reset", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && model.ResetHistoryCommand.CanExecute(null))
                await model.ResetHistoryCommand.ExecuteAsync(null);
        };
        actions.Children.Add(reset);
        var availability = new TextBlock { TextWrapping = TextWrapping.Wrap };
        ProfileScreenLayout.Bind(availability, TextBlock.TextProperty, nameof(model.ResetAvailability));
        actions.Children.Add(availability);
        Content = ProfileScreenLayout.Create(model, "Settings", new ScrollViewer { Content = fields }, actions);
    }

    private static void AddToggle(Panel panel, string label, string property)
    {
        var toggle = new ToggleSwitch { Header = label };
        ProfileScreenLayout.Bind(toggle, ToggleSwitch.IsOnProperty, property, BindingMode.TwoWay);
        panel.Children.Add(toggle);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Wisp.App.ViewModels;

namespace Wisp.App.Views;

public sealed class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel model)
    {
        Title = "Wisp - History";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 680));
        var list = new ListView { DisplayMemberPath = nameof(HistoryEntry.Display), SelectionMode = ListViewSelectionMode.Single };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Recent sessions and pending feedback");
        ProfileScreenLayout.Bind(list, ItemsControl.ItemsSourceProperty, nameof(model.Entries));
        ProfileScreenLayout.Bind(list, ListView.SelectedItemProperty, nameof(model.Selected), BindingMode.TwoWay);
        var choice = new ComboBox
        {
            Header = "Feedback", ItemsSource = HistoryViewModel.FeedbackOptions,
            DisplayMemberPath = nameof(FeedbackOption.Label), SelectedValuePath = nameof(FeedbackOption.Type), MinWidth = 190
        };
        ProfileScreenLayout.Bind(choice, ComboBox.SelectedValueProperty, nameof(model.SelectedFeedback), BindingMode.TwoWay);
        var actions = new StackPanel { Spacing = 12 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(choice);
        buttons.Children.Add(new Button { Content = "Save feedback", Command = model.SaveFeedbackCommand, VerticalAlignment = VerticalAlignment.Bottom });
        buttons.Children.Add(new Button { Content = "Remove feedback", Command = model.RemoveFeedbackCommand, VerticalAlignment = VerticalAlignment.Bottom });
        actions.Children.Add(buttons);
        actions.Children.Add(new TextBlock { Text = "Recent 100 sessions and older pending feedback. Correct game states in Library.", TextWrapping = TextWrapping.Wrap });
        Content = ProfileScreenLayout.Create(model, "History", list, actions);
    }
}

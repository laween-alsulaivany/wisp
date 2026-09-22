using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Wisp.App.ViewModels;

namespace Wisp.App.Views;

public sealed class LibraryWindow : Window
{
    public LibraryWindow(LibraryViewModel model)
    {
        Title = "Wisp - Library";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(820, 640));
        var list = new ListView { DisplayMemberPath = nameof(LibraryEntry.Display), SelectionMode = ListViewSelectionMode.Single };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Library games");
        ProfileScreenLayout.Bind(list, ItemsControl.ItemsSourceProperty, nameof(model.Entries));
        ProfileScreenLayout.Bind(list, ListView.SelectedItemProperty, nameof(model.Selected), BindingMode.TwoWay);
        var state = new ComboBox
        {
            Header = "Correct selected game state", ItemsSource = LibraryViewModel.StateOptions,
            DisplayMemberPath = nameof(StateOption.Label), SelectedValuePath = nameof(StateOption.State), MinWidth = 220
        };
        ProfileScreenLayout.Bind(state, ComboBox.SelectedValueProperty, nameof(model.TargetState), BindingMode.TwoWay);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        actions.Children.Add(state);
        actions.Children.Add(new Button { Content = "Apply state", Command = model.RestoreCommand, VerticalAlignment = VerticalAlignment.Bottom });
        Content = ProfileScreenLayout.Create(model, "Library", list, actions);
    }
}

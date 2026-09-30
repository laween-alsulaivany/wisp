using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Wisp.App.ViewModels;

namespace Wisp.App.Views;

internal static class ProfileScreenLayout
{
    internal static void Bind(FrameworkElement control, DependencyProperty property, string path,
        BindingMode mode = BindingMode.OneWay) =>
        control.SetBinding(property, new Binding { Path = new PropertyPath(path), Mode = mode });

    internal static Grid Create(ProfileScreenViewModel model, string title, FrameworkElement body, FrameworkElement actions)
    {
        var root = new Grid { Padding = new Thickness(24), RowSpacing = 16, DataContext = model };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = title, FontSize = 28 });
        var bodyHost = new ContentControl { Content = body, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        var actionsHost = new ContentControl { Content = actions, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(bodyHost, 1);
        Grid.SetRow(actionsHost, 2);
        root.Children.Add(bodyHost);
        root.Children.Add(actionsHost);
        Bind(bodyHost, Control.IsEnabledProperty, nameof(model.CanEdit));
        Bind(actionsHost, Control.IsEnabledProperty, nameof(model.CanEdit));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Bind(status, TextBlock.TextProperty, nameof(model.Status));
        Grid.SetRow(status, 3);
        root.Children.Add(status);
        return root;
    }
}

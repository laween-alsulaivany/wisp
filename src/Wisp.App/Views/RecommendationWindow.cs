using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Wisp.App.ViewModels;
using Wisp.Core.Entities;
using Wisp.Core.Enums;

namespace Wisp.App.Views;

public sealed class RecommendationWindow : Window
{
    private readonly RecommendationViewModel model;
    private readonly Image image = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock name = new() { FontSize = 24, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock reasons = new() { TextWrapping = TextWrapping.Wrap };

    public RecommendationWindow(RecommendationViewModel model)
    {
        this.model = model;
        Title = "Wisp - Pick for me";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(680, 660));
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(24), DataContext = model };
        var artwork = new Grid { Height = 240, Background = new SolidColorBrush(Microsoft.UI.Colors.DarkSlateGray) };
        artwork.Children.Add(new TextBlock
        {
            Text = "WISP", FontSize = 48, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        });
        artwork.Children.Add(image);
        artwork.Children.Add(new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(12),
            Child = new TextBlock { Text = "Play", HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) }
        });
        var play = new Button
        {
            Content = artwork, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Command = model.PlayCommand
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(play, "Play recommended game");
        panel.Children.Add(play);
        panel.Children.Add(name);
        panel.Children.Add(new Expander
        {
            Header = "Why this?", IsExpanded = false, Content = reasons,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        });

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var moods = new ComboBox { Header = "Mood", DisplayMemberPath = nameof(MoodChoice.Label),
            SelectedValuePath = nameof(MoodChoice.Value), MinWidth = 160,
            ItemsSource = new MoodChoice[] { new("Anything", MoodFilter.Anything), new("Low energy", MoodFilter.LowEnergy),
                new("Normal", MoodFilter.Normal), new("High energy", MoodFilter.HighEnergy) } };
        moods.SetBinding(ComboBox.SelectedValueProperty, new Binding { Path = new PropertyPath(nameof(model.Mood)), Mode = BindingMode.TwoWay });
        var times = new ComboBox { Header = "Time", DisplayMemberPath = nameof(TimeChoice.Label),
            SelectedValuePath = nameof(TimeChoice.Value), MinWidth = 180,
            ItemsSource = new TimeChoice[] { new("Any", default), new("Quick · ~30 min", new(TimeSpan.FromMinutes(30))),
                new("Typical · ~1h 15m", new(TimeSpan.FromMinutes(75))), new("Long · ~2h 30m", new(TimeSpan.FromMinutes(150))),
                new("Extended · ~4h", new(TimeSpan.FromMinutes(240))) } };
        times.SetBinding(ComboBox.SelectedValueProperty, new Binding { Path = new PropertyPath(nameof(model.Time)), Mode = BindingMode.TwoWay });
        filters.Children.Add(moods);
        filters.Children.Add(times);
        filters.Children.Add(new Expander { Header = "More filters", IsExpanded = false,
            Content = new TextBlock { Text = "Coming soon" }, VerticalAlignment = VerticalAlignment.Bottom });
        panel.Children.Add(filters);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(model.Status)) });
        panel.Children.Add(status);
        panel.Children.Add(new Button { Content = "Reroll", Command = model.RerollCommand,
            HorizontalAlignment = HorizontalAlignment.Center });
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        model.PropertyChanged += Refresh;
        model.Played += OnPlayed;
        Closed += (_, _) => { model.PropertyChanged -= Refresh; model.Played -= OnPlayed; model.Dispose(); };
    }

    private void OnPlayed(object? sender, EventArgs args) => Close();
    private void Refresh(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(model.Recommendation))
        {
            name.Text = model.Recommendation?.Game.Name ?? string.Empty;
            reasons.Text = string.Join("\n\n", model.Recommendation?.Reasons ?? []);
        }
        if (args.PropertyName == nameof(model.ArtworkPath))
            image.Source = model.ArtworkPath is { } path && Uri.TryCreate(path, UriKind.Absolute, out var uri)
                ? new BitmapImage(uri) : null;
    }

    public sealed record MoodChoice(string Label, MoodFilter Value);
    public sealed record TimeChoice(string Label, TimeFilter Value);
}

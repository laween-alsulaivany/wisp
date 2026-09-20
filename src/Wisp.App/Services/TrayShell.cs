using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wisp.App.Services.Hosting;
using Wisp.Core.Interfaces;
using Wisp.Core.Enums;

namespace Wisp.App.Services;

public sealed class TrayShell : IDisposable
{
    private readonly TaskbarIcon icon;
    private readonly System.Drawing.Icon drawingIcon;
    private readonly UpdateStatus updates;
    private readonly IHotkeyManager hotkey;
    private readonly ILogger<TrayShell> logger;
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<string, Window> windows = [];
    private readonly MenuFlyout menu = new();
    private readonly MenuFlyoutItem updateItem;
    private bool disposed;
    private readonly Action openRecommendation;

    public TrayShell(UpdateStatus updates, IHotkeyManager hotkey, ILogger<TrayShell> logger,
        Action openRecommendation, Func<Task> exit)
    {
        this.updates = updates;
        this.hotkey = hotkey;
        this.logger = logger;
        this.openRecommendation = openRecommendation;
        menu.Items.Add(new MenuFlyoutItem { Text = "Pick for me", Command = new RelayCommand(openRecommendation) });
        foreach (var title in new[] { "Library", "History", "Settings" })
            menu.Items.Add(new MenuFlyoutItem { Text = title, Command = new RelayCommand(() => OpenPlaceholder(title)) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Exit", Command = new AsyncRelayCommand(exit) });
        updateItem = new MenuFlyoutItem
        {
            Text = "Update available", Command = new AsyncRelayCommand(OpenReleaseAsync)
        };
        drawingIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "assets", "wisp.ico"));
        icon = new TaskbarIcon
        {
            ToolTipText = "Wisp", Icon = drawingIcon,
            ContextFlyout = menu, ContextMenuMode = ContextMenuMode.PopupMenu
        };
        updates.Changed += OnUpdateChanged;
        RefreshUpdate();
        icon.ForceCreate(enablesEfficiencyMode: false);
        if (!icon.IsCreated) throw new InvalidOperationException("The tray icon was not created.");
        hotkey.PickHotkeyPressed += OnPickHotkey;
        if (!hotkey.RegisterPickHotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x50))
            logger.LogWarning("Pick hotkey unavailable; tray remains available");
        logger.LogInformation("Tray icon created; five shell menu commands ready");
    }

    private void OnPickHotkey(object? sender, EventArgs args) =>
        dispatcher.TryEnqueue(() => { if (!disposed) openRecommendation(); });

    private void OpenPlaceholder(string title)
    {
        if (disposed) return;
        if (!windows.TryGetValue(title, out var window))
        {
            window = new Window { Title = $"Wisp - {title}", Content = new Grid() };
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(640, 480));
            window.AppWindow.Closing += (_, args) =>
            {
                if (disposed) return;
                args.Cancel = true;
                window.AppWindow.Hide();
            };
            windows.Add(title, window);
            window.Closed += (_, _) => windows.Remove(title);
        }
        window.Activate();
        logger.LogInformation("Opened {WindowTitle} placeholder", title);
    }

    private void OnUpdateChanged(object? sender, EventArgs args) => dispatcher.TryEnqueue(RefreshUpdate);

    private void RefreshUpdate()
    {
        if (disposed) return;
        if (updates.Current.UpdateAvailable)
        {
            if (!menu.Items.Contains(updateItem)) menu.Items.Insert(menu.Items.Count - 1, updateItem);
            icon.ToolTipText = "Wisp - Update available";
        }
        else
        {
            menu.Items.Remove(updateItem);
            icon.ToolTipText = "Wisp";
        }
    }

    private async Task OpenReleaseAsync()
    {
        if (updates.Current is { UpdateAvailable: true, ReleasePageUrl: { } url })
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url)))
                logger.LogWarning("Could not open the release page");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        updates.Changed -= OnUpdateChanged;
        hotkey.PickHotkeyPressed -= OnPickHotkey;
        icon.Dispose();
        drawingIcon.Dispose();
        foreach (var window in windows.Values.ToArray()) window.Close();
        windows.Clear();
    }
}

using Wisp.App.ViewModels;

namespace Wisp.App.Services;

public sealed class WindowsUriLauncher : IUriLauncher
{
    public async Task<bool> LaunchAsync(Uri uri) => await Windows.System.Launcher.LaunchUriAsync(uri);
}

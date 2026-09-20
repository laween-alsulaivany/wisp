namespace Wisp.App.ViewModels;

public interface IUriLauncher
{
    Task<bool> LaunchAsync(Uri uri);
}

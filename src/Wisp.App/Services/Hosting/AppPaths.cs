namespace Wisp.App.Services.Hosting;

public sealed record AppPaths(string DataDirectory)
{
    public static AppPaths Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wisp"));
    public string DatabasePath => Path.Combine(DataDirectory, "wisp.db");
    public string LogPath => Path.Combine(DataDirectory, "logs", "wisp-.log");
    public string CompletionEstimatesPath => Path.Combine(AppContext.BaseDirectory, "assets", "completion-estimates.db");
}

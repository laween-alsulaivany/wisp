using System.Globalization;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Manifests;

public sealed class LibraryFoldersParser : ILibraryFoldersParser
{
    /// <summary>Returns library base directories; ParseLibrary appends steamapps.</summary>
    public IReadOnlyList<string> GetLibraryPaths(string steamPath)
    {
        var path = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(path))
            path = Path.Combine(steamPath, "config", "libraryfolders.vdf");

        var folders = TextVdf.ReadRoot(path, "libraryfolders");
        var paths = new List<string>();
        if (folders is null)
            return paths;

        foreach (var (key, folder) in folders)
        {
            if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                continue;

            // Older libraryfolders files store paths directly in the numeric entries.
            var libraryPath = TextVdf.String(TextVdf.Child(folder, "path")) ?? TextVdf.String(folder);
            if (!string.IsNullOrWhiteSpace(libraryPath))
                paths.Add(libraryPath);
        }
        return paths;
    }
}

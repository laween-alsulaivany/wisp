using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Manifests;

public sealed class AcfManifestParser : IAcfManifestParser
{
    /// <summary>Scans steamapps beneath a library base directory.</summary>
    public IReadOnlyList<InstalledGameManifest> ParseLibrary(string libraryPath)
    {
        var manifests = new List<InstalledGameManifest>();
        var steamAppsPath = Path.Combine(libraryPath, "steamapps");
        if (!Directory.Exists(steamAppsPath))
            return manifests;

        foreach (var path in Directory.EnumerateFiles(steamAppsPath, "appmanifest_*.acf"))
        {
            var app = TextVdf.ReadRoot(path, "AppState");
            var appId = TextVdf.NonNegativeInteger(TextVdf.Child(app, "appid"));
            var name = TextVdf.String(TextVdf.Child(app, "name"));
            var installDir = TextVdf.String(TextVdf.Child(app, "installdir"));
            if (appId == 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
                continue;

            var stateFlags = TextVdf.NonNegativeInteger(TextVdf.Child(app, "StateFlags"));
            const long fullyInstalled = 4;
            manifests.Add(new InstalledGameManifest(appId, name, (stateFlags & fullyInstalled) != 0,
                Path.GetFullPath(Path.Combine(steamAppsPath, "common", installDir))));
        }
        return manifests;
    }
}

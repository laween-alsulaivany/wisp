using System.Globalization;
using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Manifests;

public sealed class LocalConfigParser : ILocalConfigParser
{
    public IReadOnlyDictionary<long, PlaytimeRecord> ParsePlaytime(string steamPath, string steamId3)
    {
        var records = new Dictionary<long, PlaytimeRecord>();
        var apps = TextVdf.ReadRoot(
            Path.Combine(steamPath, "userdata", steamId3, "config", "localconfig.vdf"),
            "UserLocalConfigStore");
        foreach (var key in new[] { "Software", "Valve", "Steam", "apps" })
            apps = TextVdf.Child(apps, key);

        if (apps?.IsCollection != true)
            return records;

        foreach (var (key, app) in apps)
        {
            if (!long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0)
                continue;

            var minutes = TextVdf.NonNegativeInteger(TextVdf.Child(app, "Playtime"));
            var seconds = TextVdf.NonNegativeInteger(TextVdf.Child(app, "LastPlayed"));
            DateTimeOffset? lastPlayed = seconds > 0 && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
                ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
            records[appId] = new PlaytimeRecord(minutes, lastPlayed);
        }
        return records;
    }
}

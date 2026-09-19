using System.Globalization;
using System.Security;
using Microsoft.Win32;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Manifests;

public sealed class SteamLocator : ISteamLocator
{
    private readonly Func<RegistryHive, string, string, string?> readRegistryValue;

    public SteamLocator() : this(ReadRegistryValue) { }

    internal SteamLocator(Func<RegistryHive, string, string, string?> readRegistryValue) =>
        this.readRegistryValue = readRegistryValue;

    public bool TryGetSteamInstallPath(out string steamPath)
    {
        var path = readRegistryValue(RegistryHive.CurrentUser, @"SOFTWARE\Valve\Steam", "SteamPath");
        if (string.IsNullOrWhiteSpace(path))
            path = readRegistryValue(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");

        steamPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        return steamPath.Length > 0;
    }

    public bool TryGetActiveSteamId3(string steamPath, out string steamId3, out string accountName)
    {
        steamId3 = string.Empty;
        accountName = string.Empty;
        var users = TextVdf.ReadRoot(Path.Combine(steamPath, "config", "loginusers.vdf"), "users");
        if (users is null)
            return false;

        var selectedMostRecent = false;
        long selectedTimestamp = -1;
        foreach (var (key, user) in users)
        {
            // Individual SteamID64s encode the userdata account ID in the low 32 bits.
            const ulong individualAccountBase = 76561197960265728;
            if (!ulong.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId64)
                || steamId64 <= individualAccountBase || steamId64 - individualAccountBase > uint.MaxValue)
                continue;

            var name = TextVdf.String(TextVdf.Child(user, "AccountName"));
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var mostRecent = TextVdf.NonNegativeInteger(TextVdf.Child(user, "MostRecent")) == 1;
            var timestamp = TextVdf.NonNegativeInteger(TextVdf.Child(user, "Timestamp"));
            if (steamId3.Length > 0 && (selectedMostRecent && !mostRecent
                || selectedMostRecent == mostRecent && timestamp <= selectedTimestamp))
                continue;

            steamId3 = (steamId64 - individualAccountBase).ToString(CultureInfo.InvariantCulture);
            accountName = name;
            selectedMostRecent = mostRecent;
            selectedTimestamp = timestamp;
        }
        return steamId3.Length > 0;
    }

    private static string? ReadRegistryValue(RegistryHive hive, string path, string name)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}

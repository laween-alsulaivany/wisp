namespace Wisp.Core.Interfaces;

public interface ISteamLocator
{
    bool TryGetSteamInstallPath(out string steamPath);
    bool TryGetActiveSteamId3(string steamPath, out string steamId3, out string accountName);
}

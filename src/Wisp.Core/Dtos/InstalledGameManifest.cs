namespace Wisp.Core.Dtos;

/// <summary>
/// Sections 3.3 and 4.3 require the app identity, name, installed state, and
/// absolute installation directory for matching running games to library entries.
/// </summary>
public sealed record InstalledGameManifest(
    long AppId,
    string Name,
    bool Installed,
    string InstallDir);

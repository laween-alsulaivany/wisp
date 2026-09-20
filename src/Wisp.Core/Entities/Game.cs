namespace Wisp.Core.Entities;

public sealed record Game
{
    public int GameId { get; init; }
    public long AppId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public string? InstallDir { get; init; }
    public bool IsFreeToPlay { get; init; }
    public bool IsToolOrUtility { get; init; }
    public bool IsDemo { get; init; }
    public bool IsVrOnly { get; init; }
    public bool SupportsController { get; init; }
    public bool IsSinglePlayer { get; init; }
    public bool IsMultiplayer { get; init; }
    public bool IsStoryFocused { get; init; }
    public string? HeaderImagePath { get; init; }
    public DateTimeOffset? HeaderImageFetchedUtc { get; init; }
    public DateTimeOffset? MetadataFetchedUtc { get; init; }
    public bool MetadataStale { get; init; } = true;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public long SteamCumulativePlaytimeMinutes { get; init; }
    public DateTimeOffset? SteamLastPlayedUtc { get; init; }
}

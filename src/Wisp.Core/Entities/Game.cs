namespace Wisp.Core.Entities;

public sealed record Game
{
    public int GameId { get; init; }
    public long AppId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public bool IsFreeToPlay { get; init; }
    public bool IsToolOrUtility { get; init; }
    public bool IsDemo { get; init; }
    public bool IsVrOnly { get; init; }
    public bool SupportsController { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public long SteamCumulativePlaytimeMinutes { get; init; }
    public DateTimeOffset? SteamLastPlayedUtc { get; init; }
}

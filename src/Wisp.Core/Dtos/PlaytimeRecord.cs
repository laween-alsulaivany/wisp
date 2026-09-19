namespace Wisp.Core.Dtos;

/// <summary>
/// Sections 3.3 and 3.1 require cumulative Steam playtime in minutes and an
/// optional last-played timestamp; the parser dictionary supplies the app identity.
/// </summary>
public sealed record PlaytimeRecord(
    long SteamCumulativePlaytimeMinutes,
    DateTimeOffset? SteamLastPlayedUtc);

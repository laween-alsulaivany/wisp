namespace Wisp.Core.Dtos;

/// <summary>
/// Sections 2.3 and 5.1 allow finished, demo, and VR-only games to be included;
/// the supplied UTC time is the comparison point for Maybe Later cooldowns.
/// </summary>
public sealed record EligibilityFilter
{
    public DateTimeOffset UtcNow { get; init; }
    public bool IncludeFinishedGames { get; init; }
    public bool IncludeDemos { get; init; }
    public bool IncludeVrOnly { get; init; }
}

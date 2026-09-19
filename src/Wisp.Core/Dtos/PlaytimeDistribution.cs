namespace Wisp.Core.Dtos;

/// <summary>
/// Sections 4.3 and 5.2 require logged active foreground durations to determine
/// sample counts, personal medians, and profile-wide bucket medians in the engine.
/// </summary>
public sealed record PlaytimeDistribution
{
    public IReadOnlyList<int> ActiveForegroundSeconds { get; init; } = Array.Empty<int>();
}

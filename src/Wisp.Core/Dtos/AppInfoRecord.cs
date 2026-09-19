namespace Wisp.Core.Dtos;

/// <summary>
/// Sections 2.2, 3.4, and 5.1 require app-keyed classification flags, controller
/// support, and local tag and genre identifiers for metadata and eligibility.
/// </summary>
public sealed record AppInfoRecord
{
    public long AppId { get; init; }
    public bool IsFreeToPlay { get; init; }
    public bool IsToolOrUtility { get; init; }
    public bool IsDemo { get; init; }
    public bool IsVrOnly { get; init; }
    public bool SupportsController { get; init; }
    public bool IsSinglePlayer { get; init; }
    public bool IsMultiplayer { get; init; }
    public bool IsStoryFocused { get; init; }
    public IReadOnlyList<int> TagIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> GenreIds { get; init; } = Array.Empty<int>();
}

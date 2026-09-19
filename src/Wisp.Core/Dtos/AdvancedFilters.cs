namespace Wisp.Core.Dtos;

/// <summary>
/// Sections 2.2, 2.3, 3.4, and 5.1 supply classification, tag, genre, and
/// inclusion fields for optional filters. Section 3.1 requires an empty None value.
/// </summary>
public sealed record AdvancedFilters
{
    public static AdvancedFilters None { get; } = new();

    public bool StoryFocusedOnly { get; init; }
    public bool SinglePlayerOnly { get; init; }
    public bool MultiplayerOnly { get; init; }
    public bool ControllerFriendlyOnly { get; init; }
    public IReadOnlyList<int> GenreIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public bool IncludeFinishedGames { get; init; }
    public bool IncludeDemos { get; init; }
    public bool IncludeVrOnly { get; init; }
}

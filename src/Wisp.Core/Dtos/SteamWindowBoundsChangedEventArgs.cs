namespace Wisp.Core.Dtos;

/// <summary>
/// Section 4.1 requires extended frame edges in physical screen pixels and the
/// current window DPI to position and scale the attached button across monitors.
/// </summary>
public sealed record SteamWindowBoundsChangedEventArgs(
    int Left,
    int Top,
    int Right,
    int Bottom,
    uint Dpi);

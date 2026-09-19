namespace Wisp.Core.Dtos;

/// <summary>
/// Section 3.2 references the section 1.5 notify-only contract: whether a newer
/// version exists and its release-page URL, absent when no update is available.
/// </summary>
public sealed record UpdateCheckResult(bool UpdateAvailable, string? ReleasePageUrl);

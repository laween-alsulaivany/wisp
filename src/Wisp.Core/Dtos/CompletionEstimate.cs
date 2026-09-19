namespace Wisp.Core.Dtos;

/// <summary>
/// Sections 2.2 and 2.4 define the app identity, nullable completion durations in
/// minutes, and bundled source version; section 5.2 uses the main-story estimate.
/// </summary>
public sealed record CompletionEstimate(
    long AppId,
    int? MainStoryMinutes,
    int? MainPlusExtraMinutes,
    int? CompletionistMinutes,
    string SourceVersion);

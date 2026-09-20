namespace Wisp.Core.Dtos;

/// <summary>
/// Section 4.1 reports every foreground process transition independently of Steam.
/// Null means that no foreground process can currently be identified.
/// </summary>
public sealed record ForegroundProcessChangedEventArgs(int? ProcessId);

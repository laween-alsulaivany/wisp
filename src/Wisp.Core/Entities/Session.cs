using Wisp.Core.Enums;

namespace Wisp.Core.Entities;

public sealed record Session
{
    public int SessionId { get; init; }
    public int GameId { get; init; }
    public int ProfileId { get; init; }
    public DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset? EndUtc { get; init; }
    public int? RuntimeSeconds { get; init; }
    public int ActiveForegroundSeconds { get; init; }
    public int RestartCount { get; init; }
    public LaunchSource LaunchSource { get; init; }
}

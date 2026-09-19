using Wisp.Core.Enums;

namespace Wisp.Core.Entities;

public sealed record GameState
{
    public int GameId { get; init; }
    public int ProfileId { get; init; }
    public GameStateKind State { get; init; }
    public double ActiveRankScore { get; init; }
    public DateTimeOffset? MaybeLaterUntilUtc { get; init; }
    public DateTimeOffset StateChangedUtc { get; init; }
}

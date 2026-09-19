using Wisp.Core.Enums;

namespace Wisp.Core.Entities;

/// <summary>
/// Sections 2.2 and 3.2 require feedback and session/game/profile identities,
/// the feedback type, nullable recorded time, and pending/edit status for persistence.
/// </summary>
public sealed record Feedback
{
    public int FeedbackId { get; init; }
    public int SessionId { get; init; }
    public int GameId { get; init; }
    public int ProfileId { get; init; }
    public FeedbackType FeedbackType { get; init; }
    public DateTimeOffset? RecordedUtc { get; init; }
    public bool IsPending { get; init; }
    public bool Edited { get; init; }
}

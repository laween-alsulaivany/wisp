using Wisp.Core.Entities;

namespace Wisp.Core.Dtos;

/// <summary>
/// Section 4.3 raises one start event per logical session; the session carries
/// its persisted identity, game/profile, start time, and launch source from section 3.1.
/// </summary>
public sealed record SessionStartedEventArgs(Session Session);

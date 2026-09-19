using Wisp.Core.Entities;

namespace Wisp.Core.Dtos;

/// <summary>
/// Section 4.3 raises one end event after finalization; the session carries its
/// identity, end time, runtime, foreground duration, and restart count from section 3.1.
/// </summary>
public sealed record SessionEndedEventArgs(Session Session);

using System;
using System.Collections.Generic;
using System.Text;

namespace Wisp.Core.Entities;

public sealed record SteamProfile
{
    public int ProfileId { get; init; }
    public string SteamId64 { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string? PersonaName { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
}
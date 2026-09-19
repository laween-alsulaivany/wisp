using System;
using System.Collections.Generic;
using System.Text;

namespace Wisp.Core.Entities;

public sealed record ProfileSettings
{
    public int ProfileId { get; init; }
    public int MaybeLaterCooldownDays { get; init; } = 7;
    public bool IncludeFinishedGames { get; init; }
    public bool IncludeDemos { get; init; }
    public bool IncludeVrOnly { get; init; }
    public bool StartWithWindows { get; init; } = true;
    public bool ShowPostSessionFeedback { get; init; } = true;
    public bool AdvancedFiltersExpanded { get; init; }
}
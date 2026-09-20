using Wisp.Core.Entities;
using Wisp.Core.Enums;

namespace Wisp.RecommendationEngine.Tests;

internal static class RecommendationFixtures
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    internal static RecommendationCandidate Candidate(int id, double? minutes = null, string tag = "Puzzle") => new()
    {
        Game = new Game { GameId = id, AppId = id, Name = $"Game {id}", Installed = true, Tags = [tag] },
        LoggedSessionCount = minutes is null ? 0 : 3,
        PersonalMedianSessionMinutes = minutes
    };

    internal static RecommendationCandidate Active(int id, double rank, double days = 0) => Candidate(id) with
    {
        State = GameStateKind.Active,
        StoredActiveRankScore = rank,
        StateChangedUtc = Now.AddDays(-days)
    };

    internal static RecommendationRequest Request(double? minutes = null, MoodFilter mood = MoodFilter.Anything) => new()
    {
        ProfileId = 1, Mood = mood,
        Time = new(minutes is { } value ? TimeSpan.FromMinutes(value) : null)
    };

    internal static RecommendationInputSnapshot Snapshot(params RecommendationCandidate[] candidates) => new()
    {
        CurrentTimeUtc = Now, Candidates = candidates
    };
}

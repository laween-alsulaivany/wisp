using Dapper;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;

namespace Wisp.Data;

public sealed class RecommendationSnapshotProvider(
    WispDatabase database, IGameRepository games, ISettingsRepository settings, IClock clock)
    : IRecommendationSnapshotProvider
{
    public async Task<RecommendationInputSnapshot> BuildSnapshotAsync(int profileId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var now = clock.UtcNow;
        var profileSettings = await settings.GetAsync(profileId, ct) ?? new ProfileSettings { ProfileId = profileId };
        var pool = await games.GetEligiblePoolAsync(profileId, new EligibilityFilter
        {
            UtcNow = now,
            IncludeFinishedGames = profileSettings.IncludeFinishedGames,
            IncludeDemos = profileSettings.IncludeDemos,
            IncludeVrOnly = profileSettings.IncludeVrOnly
        }, ct);

        await using var connection = await database.OpenReadAsync(ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        var sessions = (await connection.QueryAsync<LoggedSession>(WispDatabase.Command("""
            SELECT GameId, ActiveForegroundSeconds, StartUtc FROM Sessions
            WHERE ProfileId = @ProfileId AND EndUtc IS NOT NULL;
            """, new { ProfileId = profileId }, ct, transaction))).ToArray();
        var sessionsByGame = sessions.ToLookup(session => session.GameId);
        var states = (await connection.QueryAsync<GameState>(WispDatabase.Command(
            "SELECT * FROM GameStates WHERE ProfileId = @ProfileId;",
            new { ProfileId = profileId }, ct, transaction))).ToDictionary(state => state.GameId);
        var estimates = (await connection.QueryAsync<StoryEstimate>(WispDatabase.Command("""
            SELECT AppId, MainStoryMinutes FROM estimates.CompletionEstimates WHERE AppId IN @AppIds;
            """, new { AppIds = pool.Select(game => game.AppId).ToArray() }, ct, transaction)))
            .ToDictionary(estimate => estimate.AppId);

        var candidates = new List<RecommendationCandidate>(pool.Count);
        foreach (var game in pool)
        {
            ct.ThrowIfCancellationRequested();
            states.TryGetValue(game.GameId, out var state);
            estimates.TryGetValue(game.AppId, out var estimate);
            var history = sessionsByGame[game.GameId].ToArray();
            candidates.Add(new RecommendationCandidate
            {
                Game = game,
                State = state?.State ?? GameStateKind.NoData,
                StoredActiveRankScore = state?.ActiveRankScore ?? 0,
                StateChangedUtc = state?.StateChangedUtc,
                MaybeLaterUntilUtc = state?.MaybeLaterUntilUtc,
                PersonalMedianSessionMinutes = history.Length >= 3
                    ? MedianMinutes(history.Select(session => session.ActiveForegroundSeconds)) : null,
                LoggedSessionCount = history.Length,
                CompletionEstimateMainStoryMinutes = estimate?.MainStoryMinutes,
                ConsecutiveKeepGoingStreak = state?.ConsecutiveKeepGoingCount ?? 0,
                LastSessionUtc = history.Select(session => (DateTimeOffset?)session.StartUtc).Max(),
                NeverPlayed = history.Length == 0 && game.SteamCumulativePlaytimeMinutes == 0
            });
        }

        return new RecommendationInputSnapshot
        {
            CurrentTimeUtc = now,
            ProfileWideMedianSessionMinutes = MedianMinutes(sessions.Select(session => session.ActiveForegroundSeconds)),
            Candidates = candidates.ToArray()
        };
    }

    private static double? MedianMinutes(IEnumerable<int> samples)
    {
        var sorted = samples.Order().ToArray();
        if (sorted.Length == 0)
            return null;

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? ((double)sorted[middle - 1] + sorted[middle]) / 120.0
            : sorted[middle] / 60.0;
    }

    private sealed class LoggedSession
    {
        public int GameId { get; set; }
        public int ActiveForegroundSeconds { get; set; }
        public DateTimeOffset StartUtc { get; set; }
    }

    private sealed class StoryEstimate
    {
        public long AppId { get; set; }
        public int? MainStoryMinutes { get; set; }
    }
}

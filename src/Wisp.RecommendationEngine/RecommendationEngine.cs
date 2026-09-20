using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.RecommendationEngine;

public sealed class RecommendationEngine : IRecommendationEngine
{
    private readonly Random _random;

    public RecommendationEngine() : this(Random.Shared) { }

    internal RecommendationEngine(Random random) => _random = random;

    public Task<RecommendationResult?> GetRecommendationAsync(
        RecommendationRequest request, RecommendationInputSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (snapshot.Candidates.Count == 0)
            return Task.FromResult<RecommendationResult?>(null);

        var tier = 1;
        var pool = RecommendationPool.ForTier(request, snapshot, tier);
        while (tier < RecommendationConstants.FinalTier && pool.Length < RecommendationConstants.MinPoolSize)
            pool = RecommendationPool.ForTier(request, snapshot, ++tier);

        // Reconstruct the starting tier before applying session exclusions, so a reroll
        // retains its tier even when fewer than MinPoolSize unseen games remain.
        var remaining = Unseen(pool, request);
        while (remaining.Length == 0 && tier < RecommendationConstants.FinalTier)
            remaining = Unseen(RecommendationPool.ForTier(request, snapshot, ++tier), request);

        if (remaining.Length == 0)
            remaining = snapshot.Candidates.ToArray();

        ct.ThrowIfCancellationRequested();
        var scored = RecommendationScoring.Score(remaining, request, snapshot);
        var selected = WeightedSelection.Select(scored, _random);
        return Task.FromResult<RecommendationResult?>(new RecommendationResult
        {
            Game = selected.Candidate.Game,
            Score = selected.TotalScore,
            FallbackTierUsed = tier,
            Reasons = RecommendationReasons.Generate(RecommendationReasons.Context(selected, scored, snapshot.CurrentTimeUtc))
        });
    }

    private static RecommendationCandidate[] Unseen(
        IReadOnlyList<RecommendationCandidate> pool, RecommendationRequest request) =>
        pool.Where(candidate => !request.ExcludedGameIdsThisSession.Contains(candidate.Game.GameId)).ToArray();
}

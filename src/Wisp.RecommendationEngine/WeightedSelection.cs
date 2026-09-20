namespace Wisp.RecommendationEngine;

internal static class WeightedSelection
{
    internal static ScoredCandidate Select(IReadOnlyList<ScoredCandidate> pool, Random random)
    {
        var weights = pool.Select(item => Math.Exp(item.TotalScore / RecommendationConstants.Temperature)).ToArray();
        var draw = random.NextDouble() * weights.Sum();
        for (var index = 0; index < pool.Count; index++)
        {
            draw -= weights[index];
            if (draw < 0)
                return pool[index];
        }

        return pool[^1]; // Floating-point accumulation can leave a tiny positive remainder.
    }
}

namespace Wisp.RecommendationEngine;

internal static class RecommendationConstants
{
    internal const int MinPoolSize = 3;
    internal const int FinalTier = 5;
    internal const double HalfLifeDays = 45;
    internal const double BaselineConstant = 0.30;
    internal const double TightToleranceMinutes = 30;
    internal const double LooseToleranceMinutes = 90;
    internal const double ToleranceWindowMinutes = 90;
    internal const double MinimumTimeComponent = 0.2;
    internal const double RankWeight = 0.65;
    internal const double TimeWeight = 0.35;
    internal const double DiscoveryBoost = 0.15;
    internal const double Temperature = 0.18;
    internal const int MinimumPersonalSessionCount = 3;
    internal const int MinimumKeepGoingStreak = 3;
    internal const double TopActivePercentile = 85;
    internal const double TopQuartilePercentile = 75;
    internal const double LongAbsenceDays = 90;
    internal const double DaysPerMonth = 30;
    internal const int ThinPoolSize = 5;
    internal const int MaximumReasons = 2;
}

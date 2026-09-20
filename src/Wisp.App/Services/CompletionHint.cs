namespace Wisp.App.Services;

public static class CompletionHint
{
    // Achievement percentage is a fraction from 0 to 1; missing data gives no hint.
    public static bool ShouldPrompt(double? achievementPercentage) =>
        achievementPercentage >= GameStateConstants.CompletionHintAchievementThreshold;
}

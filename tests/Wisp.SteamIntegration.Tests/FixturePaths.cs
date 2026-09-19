namespace Wisp.SteamIntegration.Tests;

internal static class FixturePaths
{
    internal static string Steam(string scenario) => Path.Combine(AppContext.BaseDirectory, "Fixtures", scenario);
}

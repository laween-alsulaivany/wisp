using Wisp.Core.Entities;
using Xunit;

namespace Wisp.Core.Tests;

public sealed class PersistenceContractTests
{
    [Fact]
    public void SteamProfilesWithIdenticalFieldsAreEqual()
    {
        var lastSeen = new DateTimeOffset(
            2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        var first = new SteamProfile
        {
            ProfileId = 1,
            SteamId64 = "76561198000000000",
            AccountName = "laween",
            PersonaName = "Laween",
            LastSeenUtc = lastSeen
        };

        var second = new SteamProfile
        {
            ProfileId = 1,
            SteamId64 = "76561198000000000",
            AccountName = "laween",
            PersonaName = "Laween",
            LastSeenUtc = lastSeen
        };

        Assert.Equal(first, second);
        Assert.NotEqual(first, second with { ProfileId = 2 });
    }

    [Fact]
    public void ProfileSettingsHaveExpectedDefaults()
    {
        var settings = new ProfileSettings
        {
            ProfileId = 1
        };

        Assert.Equal(1, settings.ProfileId);
        Assert.Equal(7, settings.MaybeLaterCooldownDays);

        Assert.False(settings.IncludeFinishedGames);
        Assert.False(settings.IncludeDemos);
        Assert.False(settings.IncludeVrOnly);

        Assert.True(settings.StartWithWindows);
        Assert.True(settings.ShowPostSessionFeedback);

        Assert.False(settings.AdvancedFiltersExpanded);
    }

    [Fact]
    public void ProfileSettingsWithIdenticalFieldsAreEqual()
    {
        var first = new ProfileSettings
        {
            ProfileId = 1,
            MaybeLaterCooldownDays = 14,
            IncludeFinishedGames = true
        };

        var second = new ProfileSettings
        {
            ProfileId = 1,
            MaybeLaterCooldownDays = 14,
            IncludeFinishedGames = true
        };

        Assert.Equal(first, second);

        Assert.NotEqual(
            first,
            second with { MaybeLaterCooldownDays = 7 });
    }
}
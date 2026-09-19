using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Manifests;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class LocalConfigParserTests
{
    private readonly ILocalConfigParser parser = new LocalConfigParser();

    [Fact]
    public void MissingPlaytimeKeyReturnsZeroAndNull()
    {
        var records = parser.ParsePlaytime(FixturePaths.Steam("LocalConfig"), "1");

        Assert.Equal(new PlaytimeRecord(0, null), records[10]);
    }

    [Fact]
    public void AllPathAndFieldLookupsAreCaseInsensitive()
    {
        var records = parser.ParsePlaytime(FixturePaths.Steam("LocalConfig"), "1");

        Assert.Equal(new PlaytimeRecord(125, DateTimeOffset.FromUnixTimeSeconds(1700000000)), records[20]);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(60)]
    [InlineData(70)]
    [InlineData(80)]
    public void MalformedOrRenamedFieldsDefaultWithoutLosingOtherApps(long appId)
    {
        var records = parser.ParsePlaytime(FixturePaths.Steam("LocalConfig"), "1");

        Assert.Equal(new PlaytimeRecord(0, null), records[appId]);
        Assert.Equal(new PlaytimeRecord(75, DateTimeOffset.FromUnixTimeSeconds(1700000100)), records[120]);
        Assert.Equal(12, records.Count);
    }

    [Fact]
    public void ZeroOrOutOfRangeLastPlayedIsNullWithoutDiscardingPlaytime()
    {
        var records = parser.ParsePlaytime(FixturePaths.Steam("LocalConfig"), "1");

        Assert.Equal(new PlaytimeRecord(45, null), records[90]);
        Assert.Equal(new PlaytimeRecord(0, DateTimeOffset.FromUnixTimeSeconds(253402300799)), records[100]);
        Assert.Equal(new PlaytimeRecord(60, null), records[110]);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("WrongShape")]
    [InlineData("Truncated")]
    public void MissingOrUnreadableAppTreeReturnsEmpty(string scenario) =>
        Assert.Empty(parser.ParsePlaytime(FixturePaths.Steam(scenario), "1"));
}

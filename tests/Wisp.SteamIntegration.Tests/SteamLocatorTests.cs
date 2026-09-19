using Microsoft.Win32;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Manifests;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class SteamLocatorTests
{
    [Theory]
    [InlineData("MostRecent", "1", "fixture_recent")]
    [InlineData("Timestamp", "2", "fixture_newest")]
    public void SelectsActiveAccountAndConvertsToUserdataDirectoryId(string scenario, string id, string name)
    {
        ISteamLocator locator = new SteamLocator((_, _, _) => throw new InvalidOperationException("Unexpected registry access."));

        Assert.True(locator.TryGetActiveSteamId3(FixturePaths.Steam(scenario), out var steamId3, out var accountName));
        Assert.Equal(id, steamId3);
        Assert.Equal(name, accountName);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("InvalidUsers")]
    public void NoValidAccountReturnsFalseAndEmptyOutputs(string scenario)
    {
        ISteamLocator locator = new SteamLocator();

        Assert.False(locator.TryGetActiveSteamId3(FixturePaths.Steam(scenario), out var steamId3, out var accountName));
        Assert.Equal(string.Empty, steamId3);
        Assert.Equal(string.Empty, accountName);
    }

    [Fact]
    public void CurrentUserSteamPathTakesPriority()
    {
        var locator = new SteamLocator((hive, path, name) =>
        {
            Assert.Equal(RegistryHive.CurrentUser, hive);
            Assert.Equal(@"SOFTWARE\Valve\Steam", path);
            Assert.Equal("SteamPath", name);
            return @"C:\Fixture Steam";
        });

        Assert.True(locator.TryGetSteamInstallPath(out var steamPath));
        Assert.Equal(@"C:\Fixture Steam", steamPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingCurrentUserPathFallsBackToMachineInstallPath(string? currentUserPath)
    {
        var calls = new List<RegistryHive>();
        var locator = new SteamLocator((hive, path, name) =>
        {
            calls.Add(hive);
            if (hive == RegistryHive.CurrentUser)
                return currentUserPath;
            Assert.Equal(@"SOFTWARE\WOW6432Node\Valve\Steam", path);
            Assert.Equal("InstallPath", name);
            return @"D:\Fixture Steam";
        });

        Assert.True(locator.TryGetSteamInstallPath(out var steamPath));
        Assert.Equal(@"D:\Fixture Steam", steamPath);
        Assert.Equal(new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }, calls);
    }

    [Fact]
    public void MissingRegistryPathsReturnFalseAndEmptyOutput()
    {
        var locator = new SteamLocator((_, _, _) => null);

        Assert.False(locator.TryGetSteamInstallPath(out var steamPath));
        Assert.Equal(string.Empty, steamPath);
    }

    [Fact]
    public void SelectedAccountCanBeUsedToReadLocalConfig()
    {
        ISteamLocator locator = new SteamLocator();
        Assert.True(locator.TryGetActiveSteamId3(FixturePaths.Steam("MostRecent"), out var steamId3, out _));

        var records = new LocalConfigParser().ParsePlaytime(FixturePaths.Steam("LocalConfig"), steamId3);

        Assert.Equal(125, records[20].SteamCumulativePlaytimeMinutes);
    }
}

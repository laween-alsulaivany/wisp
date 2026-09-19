using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Manifests;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class AcfManifestParserTests
{
    private readonly IAcfManifestParser parser = new AcfManifestParser();

    [Fact]
    public void ReadsIdentityAndAbsoluteInstallDirectoryFromLibraryBase()
    {
        var library = FixturePaths.Steam("Manifests");
        var manifest = Assert.Single(parser.ParseLibrary(library), entry => entry.AppId == 10);

        Assert.Equal(new InstalledGameManifest(10, "Fixture Installed Game", true,
            Path.Combine(library, "steamapps", "common", "Fixture Installed Game")), manifest);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(20, false)]
    [InlineData(30, true)]
    [InlineData(40, false)]
    public void InstalledUsesBitFourRatherThanEqualityOrFilePresence(long appId, bool installed)
    {
        var manifest = Assert.Single(parser.ParseLibrary(FixturePaths.Steam("Manifests")), entry => entry.AppId == appId);

        Assert.Equal(installed, manifest.Installed);
    }

    [Fact]
    public void InvalidManifestsDoNotPreventReadingOtherFiles()
    {
        var manifests = parser.ParseLibrary(FixturePaths.Steam("Manifests"));

        Assert.Equal(new long[] { 10, 20, 30, 40 }, manifests.Select(entry => entry.AppId).Order());
    }

    [Fact]
    public void NumericNamesAndDirectoriesRemainUsableText()
    {
        var library = FixturePaths.Steam("NumericName");

        Assert.Equal(new InstalledGameManifest(140, "140", true,
            Path.Combine(library, "steamapps", "common", "140")), Assert.Single(parser.ParseLibrary(library)));
    }

    [Fact]
    public void MissingSteamAppsDirectoryReturnsEmpty() =>
        Assert.Empty(parser.ParseLibrary(FixturePaths.Steam("Missing")));
}

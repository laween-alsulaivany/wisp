using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Manifests;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class LibraryFoldersParserTests
{
    private readonly ILibraryFoldersParser parser = new LibraryFoldersParser();

    [Fact]
    public void ReturnsEveryLibraryBasePathIncludingAnEmptyLibrary()
    {
        var paths = parser.GetLibraryPaths(FixturePaths.Steam("Libraries"));

        Assert.Equal(new[] { @"C:\Fixture Steam", @"D:\Fixture Library", @"E:\Empty Fixture Library" }, paths);
    }

    [Fact]
    public void PrefersModernLocationWhenBothFilesExist() =>
        Assert.DoesNotContain(@"Z:\Should Not Be Used", parser.GetLibraryPaths(FixturePaths.Steam("Libraries")));

    [Fact]
    public void FallsBackToLegacyLocationAndReadsDirectPathEntries()
    {
        var paths = parser.GetLibraryPaths(FixturePaths.Steam("LegacyLibraries"));

        Assert.Equal(new[] { @"D:\Legacy Fixture Library", @"E:\Another Fixture Library" }, paths);
    }

    [Fact]
    public void MissingLibraryFoldersReturnsEmpty() =>
        Assert.Empty(parser.GetLibraryPaths(FixturePaths.Steam("Missing")));
}

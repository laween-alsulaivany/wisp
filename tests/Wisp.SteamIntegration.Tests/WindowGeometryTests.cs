using Wisp.SteamIntegration.Win32;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class WindowGeometryTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]
    [InlineData(1, 0, 1920, 1080, false)]
    [InlineData(0, 1, 1920, 1080, false)]
    [InlineData(0, 0, 1919, 1080, false)]
    [InlineData(0, 0, 1920, 1079, false)]
    public void BigPictureRequiresExactMonitorEdges(int left, int top, int right, int bottom, bool expected)
    {
        Assert.Equal(expected, WindowGeometry.IsBigPicture("steam.exe",
            new(left, top, right, bottom), new(0, 0, 1920, 1080)));
    }

    [Fact]
    public void FullscreenUnrelatedProcessIsNotBigPicture()
    {
        var bounds = new WindowRect(-1920, -100, 0, 980);
        Assert.False(WindowGeometry.IsBigPicture("game.exe", bounds, bounds));
        Assert.True(WindowGeometry.IsBigPicture("STEAM", bounds, bounds));
    }

    [Theory]
    [InlineData(96u, 1896, 1064)]
    [InlineData(144u, 1884, 1056)]
    [InlineData(192u, 1872, 1048)]
    public void OverlayOffsetScalesFromLogicalPixels(uint dpi, double x, double y)
    {
        Assert.Equal(new OverlayAnchor(x, y), WindowGeometry.CalculateOverlayAnchor(1920, 1080, dpi, 24, 16));
    }

    [Fact]
    public void OverlaySupportsNegativeMonitorCoordinatesAndFractionalPixels()
    {
        Assert.Equal(new OverlayAnchor(-7.5, -107.5), WindowGeometry.CalculateOverlayAnchor(0, -100, 144, 5, 5));
    }

    [Fact]
    public void MainWindowIsLargestTitledSteamClientArea()
    {
        WindowCandidate[] windows =
        [
            new(1, 42, "steam", "Friends", new(0, 0, 400, 800)),
            new(2, 42, "steam", "Steam", new(0, 0, 1000, 900)),
            new(3, 42, "steam", "", new(0, 0, 4000, 3000)),
            new(4, 42, "steam", "Settings", new(0, 0, 1200, 500)),
            new(5, 99, "game", "Game", new(0, 0, 4000, 3000))
        ];
        Assert.Equal(windows[1], WindowGeometry.PickMainWindow(windows));
    }

    [Fact]
    public void EmptyWindowListHasNoMainWindow() => Assert.Null(WindowGeometry.PickMainWindow([]));

    [Fact]
    public void AreaDoesNotOverflowAtLargeDesktopSizes()
    {
        Assert.Equal(10_000_000_000L, new WindowRect(0, 0, 100_000, 100_000).Area);
        Assert.Equal(0, new WindowRect(5, 0, 0, 100).Area);
    }
}

using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.AppInfo;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class TagDictionaryProviderTests
{
    private readonly ITagDictionaryProvider provider = new TagDictionaryProvider();

    [Theory]
    [InlineData(19, "Action")]
    [InlineData(1742, "Story Rich")]
    [InlineData(1654, "Relaxing")]
    [InlineData(9, "Strategy")]
    [InlineData(1664, "Puzzle")]
    [InlineData(1685, "Co-op")]
    [InlineData(4182, "Singleplayer")]
    public void ResolvesBundledTags(int id, string expected) => Assert.Equal(expected, provider.Resolve(id));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void UnknownTagReturnsNull(int id) => Assert.Null(provider.Resolve(id));
}

using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.AppInfo;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class AppInfoParserTests
{
    private readonly IAppInfoParser parser = new AppInfoParser();

    [Theory]
    [InlineData(0x07564428u)]
    [InlineData(0x07564429u)]
    public async Task CorruptEntryBetweenValidAppsIsSkippedAndFullStreamCompletes(uint magic)
    {
        using var fixture = new AppInfoFixture(magic);
        fixture.Entry(10, fixture.ValidBlob());
        fixture.Entry(20, fixture.ValidBlob()[..^3]); // Missing closing KV markers.
        fixture.Entry(30, fixture.ValidBlob("partial"));
        fixture.Save();

        var records = new List<AppInfoRecord>();
        var completed = false;
        var error = await Record.ExceptionAsync(async () =>
        {
            await foreach (var record in parser.ParseAsync(fixture.Path, CancellationToken.None))
                records.Add(record);
            completed = true;
        });

        Assert.Null(error);
        Assert.True(completed, "A bad appinfo entry must not prevent the rest of metadata sync from completing.");
        Assert.Equal(new long[] { 10, 30 }, records.Select(record => record.AppId));
        Assert.All(records, record =>
        {
            Assert.Equal(new[] { 19, 1742, 1654 }, record.TagIds);
            Assert.Equal(new[] { 1, 23 }, record.GenreIds);
            Assert.True(record.SupportsController);
        });
    }

    [Theory]
    [InlineData(0x07564428u)]
    [InlineData(0x07564429u)]
    public async Task MultipleKindsOfBadEntriesDoNotHideLaterValidApp(uint magic)
    {
        using var fixture = new AppInfoFixture(magic);
        fixture.Entry(10, fixture.ValidBlob());
        fixture.RawEntry(20, 3, [1, 2, 3]); // Incomplete fixed fields, known boundary.
        fixture.RawEntry(21, 0, []);
        fixture.Entry(22, fixture.Blob(output => fixture.Key(output, 0xFF, "bad-type")));
        fixture.Entry(23, fixture.Blob(output =>
        {
            fixture.Key(output, 0, "wrong-root");
            output.Write(new byte[] { 8, 8 });
        }));
        fixture.Entry(24, fixture.ValidBlob().Concat(new byte[] { 42 }).ToArray());
        fixture.Entry(25, fixture.Blob(output =>
        {
            for (var i = 0; i < 10000; i++)
                fixture.Key(output, 0, "nested");
        }));
        fixture.Entry(26, [8]); // Empty document.
        fixture.Entry(27, fixture.Blob(output =>
        {
            fixture.String(output, "appinfo", "scalar root");
            output.Write((byte)8);
        }));
        fixture.Entry(28, fixture.Blob(output =>
        {
            fixture.Key(output, 0, "appinfo");
            output.Write((byte)8);
            fixture.Key(output, 0, "second-root");
            output.Write(new byte[] { 8, 8 });
        }));
        fixture.Entry(30, fixture.ValidBlob());
        fixture.Save();

        var records = await ReadAll(fixture.Path);

        Assert.Equal(new long[] { 10, 30 }, records.Select(record => record.AppId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task ValveKeyValueStringIndexFailureIsIsolatedToOneEntry(int index)
    {
        using var fixture = new AppInfoFixture(0x07564429);
        fixture.Entry(10, fixture.ValidBlob());
        fixture.Entry(20, fixture.Blob(output =>
        {
            output.Write((byte)0);
            output.Write(index);
            output.Write(new byte[] { 8, 8 });
        }));
        fixture.Entry(30, fixture.ValidBlob());
        fixture.Save();

        Assert.Equal(new long[] { 10, 30 }, (await ReadAll(fixture.Path)).Select(record => record.AppId));
    }

    [Fact]
    public async Task UnknownMagicReturnsEmptySequence()
    {
        using var fixture = new AppInfoFixture(0xDEADBEEF);
        fixture.Entry(10, fixture.ValidBlob());
        fixture.Save();

        Assert.Empty(await ReadAll(fixture.Path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(12)]
    public async Task TruncatedHeaderReturnsEmptySequence(int length)
    {
        using var fixture = new AppInfoFixture(0x07564429);
        fixture.Save();
        File.WriteAllBytes(fixture.Path, File.ReadAllBytes(fixture.Path)[..length]);

        Assert.Empty(await ReadAll(fixture.Path));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(8L)]
    [InlineData(long.MaxValue)]
    public async Task InvalidStringTableOffsetReturnsEmptySequence(long offset)
    {
        using var fixture = new AppInfoFixture(0x07564429);
        fixture.Save();
        var bytes = File.ReadAllBytes(fixture.Path);
        BitConverter.GetBytes(offset).CopyTo(bytes, 8);
        File.WriteAllBytes(fixture.Path, bytes);

        Assert.Empty(await ReadAll(fixture.Path));
    }

    [Fact]
    public async Task UnterminatedStringTableReturnsEmptySequence()
    {
        using var fixture = new AppInfoFixture(0x07564429);
        fixture.Entry(10, fixture.ValidBlob());
        fixture.Save();
        var bytes = File.ReadAllBytes(fixture.Path);
        File.WriteAllBytes(fixture.Path, bytes[..^1]);

        Assert.Empty(await ReadAll(fixture.Path));
    }

    [Fact]
    public async Task CorruptStringCountDoesNotAllocateFromUntrustedLength()
    {
        using var fixture = new AppInfoFixture(0x07564429);
        fixture.Entry(10, fixture.ValidBlob());
        fixture.Save();
        var bytes = File.ReadAllBytes(fixture.Path);
        var offset = checked((int)BitConverter.ToInt64(bytes, 8));
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, offset);
        File.WriteAllBytes(fixture.Path, bytes);

        Assert.Empty(await ReadAll(fixture.Path));
    }

    [Fact]
    public async Task OversizedEntryIsSkippedAtItsKnownBoundary()
    {
        using var fixture = new AppInfoFixture();
        fixture.RawEntry(20, 16 * 1024 * 1024 + 1, new byte[16 * 1024 * 1024 + 1]);
        fixture.Entry(30, fixture.ValidBlob());
        fixture.Save();

        Assert.Equal(30, Assert.Single(await ReadAll(fixture.Path)).AppId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public async Task MissingSentinelOrTruncatedOuterHeaderStillCompletes(int trailingBytes)
    {
        using var fixture = new AppInfoFixture();
        fixture.Entry(10, fixture.ValidBlob());
        fixture.Save(false);
        using (var append = new FileStream(fixture.Path, FileMode.Append))
            append.Write(Enumerable.Repeat((byte)0xFF, trailingBytes).ToArray());

        Assert.Equal(10, Assert.Single(await ReadAll(fixture.Path)).AppId);
    }

    [Theory]
    [InlineData(0x07564428u)]
    [InlineData(0x07564429u)]
    public async Task TruncatedFinalEntryPreservesEarlierRecordsAndCompletes(uint magic)
    {
        using var fixture = new AppInfoFixture(magic);
        fixture.Entry(10, fixture.ValidBlob());
        fixture.RawEntry(20, uint.MaxValue, [1, 2, 3]);
        fixture.Save(false);

        Assert.Equal(10, Assert.Single(await ReadAll(fixture.Path)).AppId);
    }

    [Fact]
    public async Task SentinelStopsBeforeTrailingEntries()
    {
        using var fixture = new AppInfoFixture();
        fixture.Entry(10, fixture.ValidBlob());
        fixture.RawEntry(0, 0, []);
        fixture.Entry(30, fixture.ValidBlob());
        fixture.Save();

        Assert.Equal(10, Assert.Single(await ReadAll(fixture.Path)).AppId);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("none", null, false)]
    [InlineData("FULL", null, true)]
    [InlineData("partial", null, true)]
    [InlineData(null, "22", true)]
    [InlineData(null, "28", true)]
    [InlineData(null, "2", false)]
    public async Task ReadsControllerSupport(string? controller, string? category, bool expected)
    {
        using var fixture = new AppInfoFixture();
        fixture.Entry(10, fixture.ValidBlob(controller, category));
        fixture.Save();

        Assert.Equal(expected, Assert.Single(await ReadAll(fixture.Path)).SupportsController);
    }

    [Fact]
    public async Task MissingCommonReturnsEmptyMetadata()
    {
        using var fixture = new AppInfoFixture();
        fixture.Entry(10, fixture.Blob(output =>
        {
            fixture.Key(output, 0, "appinfo");
            output.Write(new byte[] { 8, 8 });
        }));
        fixture.Save();

        var record = Assert.Single(await ReadAll(fixture.Path));
        Assert.Empty(record.TagIds);
        Assert.Empty(record.GenreIds);
        Assert.False(record.SupportsController);
    }

    [Fact]
    public async Task IdListsUseValuesAndIgnoreDuplicatesAndInvalidNumbers()
    {
        using var fixture = new AppInfoFixture();
        fixture.Entry(10, fixture.Blob(output =>
        {
            fixture.Key(output, 0, "APPINFO");
            fixture.Key(output, 0, "COMMON");
            fixture.Key(output, 0, "STORE_TAGS");
            fixture.Integer(output, "0", 19);
            fixture.String(output, "1", "19");
            fixture.String(output, "2", "not-an-id");
            fixture.Integer(output, "3", -1);
            fixture.String(output, "4", "2147483648");
            fixture.Integer(output, "5", 0);
            fixture.Key(output, 0, "6");
            output.Write((byte)8);
            fixture.String(output, "7", "999999"); // Unknown positive IDs are retained.
            output.Write(new byte[] { 8, 8, 8, 8 });
        }));
        fixture.Save();

        Assert.Equal(new[] { 19, 999999 }, Assert.Single(await ReadAll(fixture.Path)).TagIds);
    }

    [Fact]
    public async Task MissingOrLockedFileReturnsEmptySequence()
    {
        using var fixture = new AppInfoFixture();
        Assert.Empty(await ReadAll(fixture.Path));
        fixture.Save();
        using var locked = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Empty(await ReadAll(fixture.Path));
    }

    [Fact]
    public async Task CancellationBetweenEntriesPropagatesAndReleasesFile()
    {
        using var fixture = new AppInfoFixture();
        fixture.Entry(10, fixture.ValidBlob());
        fixture.Entry(20, fixture.ValidBlob());
        fixture.Save();
        using var cancellation = new CancellationTokenSource();
        await using (var iterator = parser.ParseAsync(fixture.Path, CancellationToken.None)
            .GetAsyncEnumerator(cancellation.Token))
        {
            Assert.True(await iterator.MoveNextAsync());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await iterator.MoveNextAsync());
        }
        using var exclusive = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanRead);
    }

    private async Task<List<AppInfoRecord>> ReadAll(string path)
    {
        var records = new List<AppInfoRecord>();
        await foreach (var record in parser.ParseAsync(path, CancellationToken.None))
            records.Add(record);
        return records;
    }
}

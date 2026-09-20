using System.Runtime.CompilerServices;
using NSubstitute;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Manifests;
using Wisp.SteamIntegration.Metadata;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class MetadataSyncServiceTests
{
    [Fact]
    public async Task StartupAddsNewGameAndRetainsRemovedRowWithoutRefreshingKnownMetadata()
    {
        using var scenario = new Scenario(
            new Game { GameId = 1, AppId = 10, Installed = true, MetadataStale = false, Tags = ["Old tag"] },
            new Game { GameId = 2, AppId = 20, Installed = true, MetadataStale = false });
        scenario.Manifest(10);
        scenario.Manifest(30);
        scenario.Metadata = [new() { AppId = 10, TagIds = [1] }, new() { AppId = 30, TagIds = [2, 999, 2], SupportsController = true }];
        scenario.Tags.Resolve(1).Returns("Known");
        scenario.Tags.Resolve(2).Returns("New");
        scenario.Tags.Resolve(999).Returns((string?)null);

        await scenario.Service.RunStartupSyncAsync(CancellationToken.None);

        Assert.Equal(3, scenario.Games.Rows.Count);
        Assert.False(scenario.Games.Rows[20].Installed);
        Assert.Equal(2, scenario.Games.Rows[20].GameId);
        Assert.Equal(new[] { "Old tag" }, scenario.Games.Rows[10].Tags);
        var added = scenario.Games.Rows[30];
        Assert.True(added.Installed);
        Assert.False(added.MetadataStale);
        Assert.True(added.SupportsController);
        Assert.Equal(new[] { "New" }, added.Tags);
        Assert.Equal(scenario.Now, added.MetadataFetchedUtc);
        Assert.Equal(Path.Combine(scenario.Root, "steamapps", "common", "Game30"), added.InstallDir);
        Assert.Null(added.HeaderImagePath);
        scenario.Tags.DidNotReceive().Resolve(1);
        Assert.Equal(1, scenario.MetadataReads);
    }

    [Fact]
    public async Task FrequentDeltaCallsDoNotReparseUnchangedManifestsOrRefetchFreshMetadata()
    {
        using var scenario = new Scenario();
        scenario.Manifest(10);
        scenario.Metadata = [new() { AppId = 10, TagIds = [1] }];
        scenario.Tags.Resolve(1).Returns("Action");
        await scenario.Service.RunStartupSyncAsync(CancellationToken.None);
        scenario.Games.Writes.Clear();

        await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => scenario.Service.RunDeltaSyncAsync(CancellationToken.None)));

        Assert.Equal(1, scenario.MetadataReads);
        scenario.Manifests.Received(1).ParseLibrary(scenario.Root);
        Assert.Empty(scenario.Games.Writes);
    }

    [Fact]
    public async Task DeltaBeforeStartupHandlesFreshGameWithoutReadingAppInfo()
    {
        using var scenario = new Scenario(new Game { AppId = 10, MetadataStale = false });
        scenario.Manifest(10);
        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);

        Assert.True(scenario.Games.Rows[10].Installed);
        Assert.Equal(0, scenario.MetadataReads);
    }

    [Fact]
    public async Task DeltaOnlyReparsesChangedLibraryAndDetectsInstallAndRemoval()
    {
        using var scenario = new Scenario();
        var second = scenario.AddLibrary("second");
        scenario.Manifest(10);
        scenario.Manifest(20, second);
        scenario.Metadata = [new() { AppId = 10 }, new() { AppId = 20 }, new() { AppId = 30 }];
        await scenario.Service.RunStartupSyncAsync(CancellationToken.None);

        File.Delete(Path.Combine(second, "steamapps", "appmanifest_20.acf"));
        scenario.Manifest(30, second);
        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);

        scenario.Manifests.Received(1).ParseLibrary(scenario.Root);
        scenario.Manifests.Received(2).ParseLibrary(second);
        Assert.False(scenario.Games.Rows[20].Installed);
        Assert.True(scenario.Games.Rows[30].Installed);
        Assert.False(scenario.Games.Rows[30].MetadataStale);
        Assert.Equal(3, scenario.Games.Rows.Count);
    }

    [Fact]
    public async Task ChangedManifestUpdatesInstallStateWithoutRefetchingFreshMetadata()
    {
        using var scenario = new Scenario();
        scenario.Manifest(10);
        scenario.Metadata = [new() { AppId = 10 }];
        await scenario.Service.RunStartupSyncAsync(CancellationToken.None);

        scenario.Manifest(10, stateFlags: 1024);
        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);

        Assert.False(scenario.Games.Rows[10].Installed);
        Assert.Equal(1, scenario.MetadataReads);
    }

    [Fact]
    public async Task MissingMetadataRemainsStaleAndRetriesWhenAppInfoChanges()
    {
        using var scenario = new Scenario();
        scenario.Manifest(10);
        await scenario.Service.RunStartupSyncAsync(CancellationToken.None);
        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);
        Assert.True(scenario.Games.Rows[10].MetadataStale);
        Assert.Null(scenario.Games.Rows[10].MetadataFetchedUtc);
        Assert.Equal(1, scenario.MetadataReads);

        scenario.Metadata = [new() { AppId = 10 }];
        File.WriteAllText(Path.Combine(scenario.Root, "appcache", "appinfo.vdf"), "changed");
        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);

        Assert.Equal(2, scenario.MetadataReads);
        Assert.False(scenario.Games.Rows[10].MetadataStale);
    }

    [Fact]
    public async Task ExplicitlyStaleGameRefreshesWithoutManifestChangesAndPreservesArtwork()
    {
        using var scenario = new Scenario(new Game { AppId = 10, HeaderImagePath = "cached.jpg" });
        scenario.Manifest(10);
        scenario.Metadata = [new() { AppId = 10 }];
        await scenario.Service.RunStartupSyncAsync(CancellationToken.None);
        scenario.Games.Rows[10] = scenario.Games.Rows[10] with { MetadataStale = true };

        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);

        Assert.Equal(2, scenario.MetadataReads);
        Assert.False(scenario.Games.Rows[10].MetadataStale);
        Assert.Equal("cached.jpg", scenario.Games.Rows[10].HeaderImagePath);
    }

    [Fact]
    public async Task UnreadableLibraryDoesNotMarkStoredGamesRemoved()
    {
        using var scenario = new Scenario(new Game { AppId = 10, Installed = true, MetadataStale = false });
        scenario.Manifest(10);
        await scenario.Service.RunStartupSyncAsync(CancellationToken.None);
        scenario.Paths.Add(Path.Combine(scenario.Root, "offline-library"));
        scenario.Games.Writes.Clear();

        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);

        Assert.True(scenario.Games.Rows[10].Installed);
        Assert.Empty(scenario.Games.Writes);
    }

    [Fact]
    public async Task PartialManifestWriteDoesNotCauseRemovalAndIsRetried()
    {
        using var scenario = new Scenario();
        scenario.Manifest(10);
        scenario.Metadata = [new() { AppId = 10 }];
        await scenario.Service.RunStartupSyncAsync(CancellationToken.None);
        File.WriteAllText(Path.Combine(scenario.Root, "steamapps", "appmanifest_10.acf"), "\"AppState\" {");

        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);
        Assert.True(scenario.Games.Rows[10].Installed);

        scenario.Manifest(10, stateFlags: 1024);
        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);
        Assert.False(scenario.Games.Rows[10].Installed);
    }

    [Fact]
    public async Task CancellationDoesNotPoisonNextSync()
    {
        using var scenario = new Scenario();
        scenario.Manifest(10);
        scenario.Metadata = [new() { AppId = 10 }];
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scenario.Service.RunStartupSyncAsync(cancelled.Token));

        await scenario.Service.RunDeltaSyncAsync(CancellationToken.None);
        Assert.False(scenario.Games.Rows[10].MetadataStale);
    }

    private sealed class Scenario : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Wisp-sync-tests", Guid.NewGuid().ToString("N"));
        internal DateTimeOffset Now { get; } = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
        internal List<string> Paths { get; } = [];
        internal MemoryGameRepository Games { get; }
        internal IAcfManifestParser Manifests { get; } = Substitute.For<IAcfManifestParser>();
        internal ITagDictionaryProvider Tags { get; } = Substitute.For<ITagDictionaryProvider>();
        internal AppInfoRecord[] Metadata { get; set; } = [];
        internal int MetadataReads { get; private set; }
        internal MetadataSyncService Service { get; }

        internal Scenario(params Game[] games)
        {
            Directory.CreateDirectory(Path.Combine(Root, "steamapps"));
            Directory.CreateDirectory(Path.Combine(Root, "appcache"));
            Paths.Add(Root);
            Games = new MemoryGameRepository(games);
            var locator = Substitute.For<ISteamLocator>();
            locator.TryGetSteamInstallPath(out Arg.Any<string>()).Returns(call => { call[0] = Root; return true; });
            var folders = Substitute.For<ILibraryFoldersParser>();
            folders.GetLibraryPaths(Root).Returns(Paths);
            Manifests.ParseLibrary(Arg.Any<string>()).Returns(call => new AcfManifestParser().ParseLibrary(call.Arg<string>()));
            var appInfo = Substitute.For<IAppInfoParser>();
            appInfo.ParseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => ReadMetadata(call.Arg<CancellationToken>()));
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);
            Service = new MetadataSyncService(locator, folders, Manifests, appInfo, Tags, Games, clock);
        }

        internal string AddLibrary(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.Combine(path, "steamapps"));
            Paths.Add(path);
            return path;
        }

        internal void Manifest(long appId, string? library = null, int stateFlags = 4) =>
            File.WriteAllText(Path.Combine(library ?? Root, "steamapps", $"appmanifest_{appId}.acf"),
                $$"""
                "AppState" { "appid" "{{appId}}" "name" "Game{{appId}}" "installdir" "Game{{appId}}" "StateFlags" "{{stateFlags}}" }
                """);

        private async IAsyncEnumerable<AppInfoRecord> ReadMetadata([EnumeratorCancellation] CancellationToken ct)
        {
            MetadataReads++;
            await Task.Yield();
            foreach (var record in Metadata)
            {
                ct.ThrowIfCancellationRequested();
                yield return record;
            }
        }

        public void Dispose()
        {
            Service.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}

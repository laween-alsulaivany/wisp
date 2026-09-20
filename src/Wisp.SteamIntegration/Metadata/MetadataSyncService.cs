using System.Diagnostics;
using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Metadata;

public sealed class MetadataSyncService(
    ISteamLocator steamLocator,
    ILibraryFoldersParser libraryFoldersParser,
    IAcfManifestParser manifestParser,
    IAppInfoParser appInfoParser,
    ITagDictionaryProvider tagDictionary,
    IGameRepository games,
    IClock clock) : IMetadataSyncService, IDisposable
{
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private Dictionary<string, LibrarySnapshot> libraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> attemptedAppIds = [];
    private string? lastAppInfoPath;
    private FileStamp? lastAppInfoStamp;

    public Task RunStartupSyncAsync(CancellationToken ct) => SyncAsync(forceScan: true, ct);

    public Task RunDeltaSyncAsync(CancellationToken ct) => SyncAsync(forceScan: false, ct);

    private async Task SyncAsync(bool forceScan, CancellationToken ct)
    {
        await syncGate.WaitAsync(ct);
        try
        {
            if (!steamLocator.TryGetSteamInstallPath(out var steamPath))
                return;

            var libraryPaths = libraryFoldersParser.GetLibraryPaths(steamPath);
            if (libraryPaths.Count == 0)
                return;

            var nextLibraries = new Dictionary<string, LibrarySnapshot>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var path in libraryPaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    var files = Directory.EnumerateFiles(Path.Combine(path, "steamapps"), "appmanifest_*.acf")
                        .ToDictionary(file => file, ReadStamp, StringComparer.OrdinalIgnoreCase);
                    if (!forceScan && libraries.TryGetValue(path, out var cached)
                        && files.Count == cached.Files.Count
                        && files.All(pair => cached.Files.TryGetValue(pair.Key, out var stamp) && stamp == pair.Value))
                    {
                        nextLibraries[path] = cached;
                        continue;
                    }

                    var manifests = manifestParser.ParseLibrary(path);
                    // A partial parse during a Steam write is not evidence of an uninstall.
                    if (manifests.Count != files.Count)
                        return;
                    nextLibraries[path] = new LibrarySnapshot(files, manifests);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Cannot scan Steam libraries: {exception.Message}");
                return;
            }

            var stored = (await games.GetAllAsync(ct)).ToDictionary(game => game.AppId);
            var scanned = nextLibraries.Values.SelectMany(library => library.Manifests)
                .GroupBy(manifest => manifest.AppId)
                .ToDictionary(group => group.Key, group => group.FirstOrDefault(manifest => manifest.Installed) ?? group.First());

            foreach (var manifest in scanned.Values)
            {
                var existing = stored.GetValueOrDefault(manifest.AppId);
                var game = (existing ?? new Game { AppId = manifest.AppId }) with
                {
                    Name = manifest.Name,
                    Installed = manifest.Installed,
                    InstallDir = manifest.InstallDir
                };
                if (game != existing)
                    await games.UpsertAsync(game, ct);
                stored[game.AppId] = game;
            }

            foreach (var game in stored.Values.Where(game => game.Installed && !scanned.ContainsKey(game.AppId)))
                await games.UpsertAsync(game with { Installed = false }, ct);

            libraries = nextLibraries;
            var pending = stored.Values.Where(game => scanned.ContainsKey(game.AppId) && game.MetadataStale)
                .Select(game => game.AppId).ToHashSet();
            if (pending.Count == 0)
                return;

            var appInfoPath = Path.Combine(steamPath, "appcache", "appinfo.vdf");
            var stamp = ReadStamp(appInfoPath);
            if (forceScan || lastAppInfoPath != appInfoPath || lastAppInfoStamp != stamp)
                attemptedAppIds.Clear();
            if (pending.IsSubsetOf(attemptedAppIds))
                return;

            await foreach (var metadata in appInfoParser.ParseAsync(appInfoPath, ct))
            {
                if (!pending.Contains(metadata.AppId))
                    continue;
                var game = await games.GetByAppIdAsync(metadata.AppId, ct);
                if (game is null || !game.MetadataStale)
                    continue;
                await games.UpsertAsync(game with
                {
                    IsFreeToPlay = metadata.IsFreeToPlay,
                    IsToolOrUtility = metadata.IsToolOrUtility,
                    IsDemo = metadata.IsDemo,
                    IsVrOnly = metadata.IsVrOnly,
                    SupportsController = metadata.SupportsController,
                    IsSinglePlayer = metadata.IsSinglePlayer,
                    IsMultiplayer = metadata.IsMultiplayer,
                    IsStoryFocused = metadata.IsStoryFocused,
                    Tags = metadata.TagIds.Select(tagDictionary.Resolve).OfType<string>()
                        .Distinct(StringComparer.Ordinal).ToArray(),
                    MetadataFetchedUtc = clock.UtcNow,
                    MetadataStale = false
                }, ct);
                pending.Remove(metadata.AppId);
                attemptedAppIds.Remove(metadata.AppId);
                if (pending.Count == 0)
                    break;
            }

            // Missing/corrupt entries remain stale, but retry only when the file or requested set changes.
            attemptedAppIds.UnionWith(pending);
            lastAppInfoPath = appInfoPath;
            lastAppInfoStamp = stamp;
        }
        finally
        {
            syncGate.Release();
        }
    }

    private static FileStamp ReadStamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new FileStamp(file.Length, file.LastWriteTimeUtc) : default;
    }

    public void Dispose() => syncGate.Dispose();

    private readonly record struct FileStamp(long Length, DateTime LastWriteUtc);
    private sealed record LibrarySnapshot(
        Dictionary<string, FileStamp> Files, IReadOnlyList<InstalledGameManifest> Manifests);
}

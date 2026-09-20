using System.Diagnostics;
using System.Globalization;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Artwork;

public sealed class ArtworkFetcher : IArtworkFetcher, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly IGameRepository games;
    private readonly IClock clock;
    private readonly string cacheDirectory;
    private readonly SemaphoreSlim fetchGate = new(1, 1);

    public ArtworkFetcher(HttpClient httpClient, IGameRepository games, IClock clock)
        : this(httpClient, games, clock,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wisp", "artwork"))
    {
    }

    internal ArtworkFetcher(HttpClient httpClient, IGameRepository games, IClock clock, string cacheDirectory)
    {
        this.httpClient = httpClient;
        this.games = games;
        this.clock = clock;
        this.cacheDirectory = cacheDirectory;
    }

    public async Task<string?> FetchHeaderImageAsync(long appId, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appId);
        await fetchGate.WaitAsync(ct);
        try
        {
            var id = appId.ToString(CultureInfo.InvariantCulture);
            var path = Path.Combine(cacheDirectory, $"{id}.jpg");
            if (!File.Exists(path) && !await DownloadAsync(id, path, ct))
                return null;

            // Read after the download so a concurrent metadata sync is not overwritten with an old snapshot.
            var game = await games.GetByAppIdAsync(appId, ct);
            if (game is not null && game.HeaderImagePath != path)
                await games.UpsertAsync(game with
                {
                    HeaderImagePath = path,
                    HeaderImageFetchedUtc = clock.UtcNow
                }, ct);
            return path;
        }
        finally
        {
            fetchGate.Release();
        }
    }

    private async Task<bool> DownloadAsync(string appId, string path, CancellationToken ct)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await httpClient.GetAsync(
                $"https://steamcdn-a.akamaihd.net/steam/apps/{appId}/header.jpg",
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            Directory.CreateDirectory(cacheDirectory);
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await response.Content.CopyToAsync(output, ct);
                if (output.Length == 0)
                    return false;
            }
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException
            || (exception is OperationCanceledException && !ct.IsCancellationRequested))
        {
            Trace.TraceWarning($"Cannot fetch artwork for app {appId}: {exception.Message}");
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Cannot remove temporary artwork: {exception.Message}");
            }
        }
    }

    public void Dispose() => fetchGate.Dispose();
}

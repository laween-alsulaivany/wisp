using System.Net;
using NSubstitute;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;
using Wisp.SteamIntegration.Artwork;
using Xunit;

namespace Wisp.SteamIntegration.Tests;

public sealed class ArtworkFetcherTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task HttpFailureReturnsNullAndLeavesArtworkUncached(HttpStatusCode status)
    {
        using var scenario = new Scenario(() => new HttpResponseMessage(status));

        var result = await scenario.Fetcher.FetchHeaderImageAsync(10, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(scenario.Games.Rows[10].HeaderImagePath);
        Assert.Null(scenario.Games.Rows[10].HeaderImageFetchedUtc);
        Assert.Empty(scenario.Games.Writes);
        Assert.Empty(Directory.EnumerateFiles(scenario.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkFailureOrTimeoutReturnsNull(bool timeout)
    {
        using var scenario = new Scenario(() => throw (timeout
            ? new TaskCanceledException("Simulated timeout") : new HttpRequestException("Offline")));

        Assert.Null(await scenario.Fetcher.FetchHeaderImageAsync(10, CancellationToken.None));
        Assert.Null(scenario.Games.Rows[10].HeaderImagePath);
    }

    [Fact]
    public async Task ExplicitFetchUsesCdnAndPermanentlyCachesFileAndRepositoryPath()
    {
        byte[] image = [0xff, 0xd8, 0xff, 0xd9];
        using var scenario = new Scenario(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(image)
        });
        Assert.Equal(0, scenario.Handler.Requests);

        var paths = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => scenario.Fetcher.FetchHeaderImageAsync(10, CancellationToken.None)));

        var expectedPath = Path.Combine(scenario.Root, "10.jpg");
        Assert.All(paths, path => Assert.Equal(expectedPath, path));
        Assert.Equal(image, await File.ReadAllBytesAsync(expectedPath));
        Assert.Equal(1, scenario.Handler.Requests);
        Assert.Equal(HttpMethod.Get, scenario.Handler.Method);
        Assert.Equal("https://steamcdn-a.akamaihd.net/steam/apps/10/header.jpg", scenario.Handler.Uri?.AbsoluteUri);
        Assert.False(scenario.Handler.HasAuthorization);
        Assert.Equal(expectedPath, scenario.Games.Rows[10].HeaderImagePath);
        Assert.Equal(scenario.Now, scenario.Games.Rows[10].HeaderImageFetchedUtc);
        Assert.Single(scenario.Games.Writes);
        Assert.Single(Directory.EnumerateFiles(scenario.Root));
    }

    [Fact]
    public async Task ExistingDiskCacheIsReusedByNewFetcherWithoutNetwork()
    {
        using var scenario = new Scenario(() => throw new InvalidOperationException("Network must not be used"));
        var path = Path.Combine(scenario.Root, "10.jpg");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);

        Assert.Equal(path, await scenario.Fetcher.FetchHeaderImageAsync(10, CancellationToken.None));
        Assert.Equal(0, scenario.Handler.Requests);
        Assert.Equal(path, scenario.Games.Rows[10].HeaderImagePath);
    }

    [Fact]
    public async Task EmptyResponseIsNotCached()
    {
        using var scenario = new Scenario(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        });

        Assert.Null(await scenario.Fetcher.FetchHeaderImageAsync(10, CancellationToken.None));
        Assert.Null(scenario.Games.Rows[10].HeaderImagePath);
        Assert.Empty(Directory.EnumerateFiles(scenario.Root));
    }

    [Fact]
    public async Task FailedDownloadCanBeRetried()
    {
        var attempt = 0;
        using var scenario = new Scenario(() => ++attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });

        Assert.Null(await scenario.Fetcher.FetchHeaderImageAsync(10, CancellationToken.None));
        Assert.NotNull(await scenario.Fetcher.FetchHeaderImageAsync(10, CancellationToken.None));
        Assert.Equal(2, scenario.Handler.Requests);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutSendingRequest()
    {
        using var scenario = new Scenario(() => throw new InvalidOperationException("Network must not be used"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scenario.Fetcher.FetchHeaderImageAsync(10, cancelled.Token));

        Assert.Equal(0, scenario.Handler.Requests);
        Assert.Null(scenario.Games.Rows[10].HeaderImagePath);
    }

    private sealed class Scenario : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Wisp-artwork-tests", Guid.NewGuid().ToString("N"));
        internal DateTimeOffset Now { get; } = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
        internal MemoryGameRepository Games { get; } = new(new Game { AppId = 10 });
        internal FakeHandler Handler { get; }
        internal ArtworkFetcher Fetcher { get; }
        private readonly HttpClient client;

        internal Scenario(Func<HttpResponseMessage> response)
        {
            Directory.CreateDirectory(Root);
            Handler = new FakeHandler(response);
            client = new HttpClient(Handler);
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);
            Fetcher = new ArtworkFetcher(client, Games, clock, Root);
        }

        public void Dispose()
        {
            Fetcher.Dispose();
            client.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FakeHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        internal Uri? Uri { get; private set; }
        internal HttpMethod? Method { get; private set; }
        internal bool HasAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Uri = request.RequestUri;
            Method = request.Method;
            HasAuthorization = request.Headers.Authorization is not null;
            return Task.FromResult(response());
        }
    }
}

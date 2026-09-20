using System.Net;
using System.Text;
using FluentAssertions;
using Wisp.App.Services.Hosting;
using Xunit;

namespace Wisp.App.Tests;

public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.1.0", true)]
    [InlineData("v1.0.0", false)]
    [InlineData("v0.9.0", false)]
    [InlineData("1.0", false)]
    [InlineData("v1.1.0+build.1", true)]
    [InlineData("v1.1.0-beta.1", false)]
    [InlineData("unknown", false)]
    public async Task Compares_release_version_with_running_version_using_one_anonymous_get(string tag, bool expected)
    {
        const string url = "https://github.com/laween-alsulaivany/wisp/releases/tag/v1.1.0";
        using var handler = new FakeHandler($$"""{"tag_name":"{{tag}}","html_url":"{{url}}","draft":false,"prerelease":false} """);
        using var http = new HttpClient(handler);
        var result = await new UpdateChecker(http, new Version(1, 0, 0, 0)).CheckForUpdateAsync(default);
        result.UpdateAvailable.Should().Be(expected);
        result.ReleasePageUrl.Should().Be(expected ? url : null);
        handler.Requests.Should().Be(1);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v2.0.0\",\"html_url\":\"https://evil.example/\"}")]
    [InlineData("{\"tag_name\":\"v2.0.0\",\"prerelease\":true}")]
    [InlineData("{\"tag_name\":\"v2.0.0\",\"draft\":true}")]
    public async Task Does_not_offer_prereleases_drafts_or_untrusted_links(string json)
    {
        using var handler = new FakeHandler(json);
        using var http = new HttpClient(handler);
        (await new UpdateChecker(http, new Version(1, 0)).CheckForUpdateAsync(default)).UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task No_published_release_is_not_an_update()
    {
        using var handler = new FakeHandler("{}", HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        (await new UpdateChecker(http, new Version(1, 0)).CheckForUpdateAsync(default)).UpdateAvailable.Should().BeFalse();
    }

    private sealed class FakeHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsoluteUri.Should().Be(UpdateChecker.ReleasesEndpoint);
            request.RequestUri.Query.Should().BeEmpty();
            request.Content.Should().BeNull();
            request.Headers.Select(header => header.Key).Should().BeEquivalentTo("User-Agent", "Accept");
            request.Headers.UserAgent.ToString().Should().Be("Wisp");
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}

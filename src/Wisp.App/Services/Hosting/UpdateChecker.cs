using System.Net;
using System.Text.Json;
using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;

namespace Wisp.App.Services.Hosting;

public sealed class UpdateChecker(HttpClient http, Version currentVersion) : IUpdateChecker
{
    public const string ReleasesEndpoint = "https://api.github.com/repos/laween-alsulaivany/wisp/releases/latest";
    private const string ReleasePrefix = "https://github.com/laween-alsulaivany/wisp/releases/tag/";

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
        // GitHub requires a User-Agent. A fixed product name contains no user/device/version data.
        request.Headers.UserAgent.ParseAdd("Wisp");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new(false, null); // No public release has been published yet.
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var release = json.RootElement;
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            return new(false, null);
        var tag = release.GetProperty("tag_name").GetString()?.TrimStart('v', 'V').Split('+')[0];
        if (!Version.TryParse(tag, out var latest))
            return new(false, null);
        var normalizedLatest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build), Math.Max(0, latest.Revision));
        var normalizedCurrent = new Version(currentVersion.Major, currentVersion.Minor,
            Math.Max(0, currentVersion.Build), Math.Max(0, currentVersion.Revision));
        if (normalizedLatest <= normalizedCurrent)
            return new(false, null);
        var url = release.GetProperty("html_url").GetString();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.AbsoluteUri.StartsWith(ReleasePrefix, StringComparison.Ordinal)
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return new(false, null);
        return new(true, uri.AbsoluteUri);
    }
}

namespace Wisp.Core.Interfaces;

public interface IArtworkFetcher
{
    Task<string?> FetchHeaderImageAsync(long appId, CancellationToken ct);
}

using Wisp.Core.Dtos;

namespace Wisp.Core.Interfaces;

public interface ICompletionEstimateRepository
{
    Task<CompletionEstimate?> GetAsync(long appId, CancellationToken ct);
}

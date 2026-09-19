using Wisp.Core.Dtos;

namespace Wisp.Core.Interfaces;

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct);
}

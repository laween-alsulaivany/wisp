using Wisp.Core.Dtos;

namespace Wisp.Core.Interfaces;

public interface IAppInfoParser
{
    IAsyncEnumerable<AppInfoRecord> ParseAsync(string appInfoVdfPath, CancellationToken ct);
}

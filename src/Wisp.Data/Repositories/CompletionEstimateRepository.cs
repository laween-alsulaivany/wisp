using Dapper;
using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;

namespace Wisp.Data.Repositories;

public sealed class CompletionEstimateRepository(WispDatabase database) : ICompletionEstimateRepository
{
    public async Task<CompletionEstimate?> GetAsync(long appId, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<EstimateRow>(WispDatabase.Command(
            "SELECT * FROM estimates.CompletionEstimates WHERE AppId = @AppId;", new { AppId = appId }, ct));
        return row is null ? null : new CompletionEstimate(row.AppId, row.MainStoryMinutes,
            row.MainPlusExtraMinutes, row.CompletionistMinutes, row.SourceVersion);
    }

    private sealed class EstimateRow
    {
        public long AppId { get; set; }
        public int? MainStoryMinutes { get; set; }
        public int? MainPlusExtraMinutes { get; set; }
        public int? CompletionistMinutes { get; set; }
        public string SourceVersion { get; set; } = string.Empty;
    }
}

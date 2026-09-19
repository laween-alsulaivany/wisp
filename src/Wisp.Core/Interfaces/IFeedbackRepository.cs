using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface IFeedbackRepository
{
    Task RecordAsync(Feedback feedback, CancellationToken ct);
    Task<IReadOnlyList<Feedback>> GetPendingAsync(int profileId, CancellationToken ct);
}

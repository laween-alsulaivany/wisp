using Wisp.Core.Entities;

namespace Wisp.Core.Interfaces;

public interface IFeedbackRepository
{
    // Upserts by SessionId. Replacing an answer marks Edited; answering a pending row does not.
    Task RecordAsync(Feedback feedback, CancellationToken ct);
    Task<IReadOnlyList<Feedback>> GetPendingAsync(int profileId, CancellationToken ct);
    Task<IReadOnlyList<Feedback>> GetAllAsync(int profileId, CancellationToken ct);
    Task RemoveAsync(int feedbackId, CancellationToken ct);
}

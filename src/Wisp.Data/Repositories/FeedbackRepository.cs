using Dapper;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.Data.Repositories;

public sealed class FeedbackRepository(WispDatabase database) : IFeedbackRepository
{
    public Task RecordAsync(Feedback feedback, CancellationToken ct) => database.WriteAsync(connection =>
        connection.ExecuteAsync(WispDatabase.Command("""
            INSERT INTO Feedback (SessionId, GameId, ProfileId, FeedbackType, RecordedUtc, IsPending, Edited)
            VALUES (@SessionId, @GameId, @ProfileId, @FeedbackType, @RecordedUtc, @IsPending, @Edited)
            ON CONFLICT(SessionId) DO UPDATE SET GameId = excluded.GameId, ProfileId = excluded.ProfileId,
                FeedbackType = excluded.FeedbackType, RecordedUtc = excluded.RecordedUtc,
                IsPending = excluded.IsPending, Edited = CASE WHEN Feedback.IsPending = 1 THEN 0 ELSE 1 END;
            """, feedback, ct)), ct);

    public async Task<IReadOnlyList<Feedback>> GetAllAsync(int profileId, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return (await connection.QueryAsync<Feedback>(WispDatabase.Command(
            "SELECT * FROM Feedback WHERE ProfileId = @ProfileId ORDER BY FeedbackId DESC;",
            new { ProfileId = profileId }, ct))).ToArray();
    }

    public Task RemoveAsync(int feedbackId, CancellationToken ct) => database.WriteAsync(connection =>
        connection.ExecuteAsync(WispDatabase.Command("DELETE FROM Feedback WHERE FeedbackId = @FeedbackId;",
            new { FeedbackId = feedbackId }, ct)), ct);

    public async Task<IReadOnlyList<Feedback>> GetPendingAsync(int profileId, CancellationToken ct)
    {
        await using var connection = await database.OpenReadAsync(ct);
        return (await connection.QueryAsync<Feedback>(WispDatabase.Command(
            "SELECT * FROM Feedback WHERE ProfileId = @ProfileId AND IsPending = 1 ORDER BY FeedbackId;",
            new { ProfileId = profileId }, ct))).ToArray();
    }
}

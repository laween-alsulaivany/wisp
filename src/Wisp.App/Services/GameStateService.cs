using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;

namespace Wisp.App.Services;

public sealed class GameStateService(
    IFeedbackRepository feedbackRepository,
    IGameStateRepository stateRepository,
    ISessionRepository sessionRepository,
    ISettingsRepository settingsRepository,
    IClock clock) : IGameStateService
{
    public async Task ApplyFeedbackAsync(int sessionId, FeedbackType feedback, CancellationToken ct)
    {
        if (!Enum.IsDefined(feedback))
            throw new ArgumentOutOfRangeException(nameof(feedback));

        ct.ThrowIfCancellationRequested();
        var session = await sessionRepository.GetByIdAsync(sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} does not exist.");
        var now = clock.UtcNow;
        GameState? updated = null;
        if (feedback != FeedbackType.Pending)
        {
            var state = await stateRepository.GetAsync(session.GameId, session.ProfileId, ct);
            if (feedback is FeedbackType.TechnicalIssue or FeedbackType.Interrupted)
            {
                if (state is { ConsecutiveKeepGoingCount: not 0 })
                    updated = state with { ConsecutiveKeepGoingCount = 0 };
            }
            else
            {
                state ??= new GameState { GameId = session.GameId, ProfileId = session.ProfileId };
                var target = feedback switch
                {
                    FeedbackType.KeepGoing => GameStateKind.Active,
                    FeedbackType.NotFeelingIt or FeedbackType.MaybeLater => GameStateKind.MaybeLater,
                    FeedbackType.Finished => GameStateKind.Finished,
                    FeedbackType.Dropped => GameStateKind.Dropped,
                    _ => throw new ArgumentOutOfRangeException(nameof(feedback))
                };
                updated = await ChangeStateAsync(state, target, now, ct);
                if (feedback == FeedbackType.KeepGoing)
                {
                    updated = updated with
                    {
                        ActiveRankScore = state.ActiveRankScore + GameStateConstants.KeepGoingRankIncrement,
                        ConsecutiveKeepGoingCount = state.ConsecutiveKeepGoingCount + 1
                    };
                }
            }
        }

        await feedbackRepository.RecordAsync(new Feedback
        {
            SessionId = sessionId,
            GameId = session.GameId,
            ProfileId = session.ProfileId,
            FeedbackType = feedback,
            IsPending = feedback == FeedbackType.Pending,
            RecordedUtc = feedback == FeedbackType.Pending ? null : now
        }, ct);

        if (updated is not null)
            await stateRepository.UpsertAsync(updated, ct);
    }

    public async Task RestoreAsync(int gameId, int profileId, GameStateKind newState, CancellationToken ct)
    {
        if (!Enum.IsDefined(newState))
            throw new ArgumentOutOfRangeException(nameof(newState));

        ct.ThrowIfCancellationRequested();
        var state = await stateRepository.GetAsync(gameId, profileId, ct)
            ?? new GameState { GameId = gameId, ProfileId = profileId };
        var updated = await ChangeStateAsync(state, newState, clock.UtcNow, ct);
        await stateRepository.UpsertAsync(updated, ct);
    }

    public async Task ResetRecommendationHistoryAsync(int profileId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await sessionRepository.ClearHistoryAsync(profileId, ct);
        await stateRepository.ClearAllAsync(profileId, ct);
    }

    private async Task<GameState> ChangeStateAsync(GameState state, GameStateKind target,
        DateTimeOffset now, CancellationToken ct)
    {
        DateTimeOffset? until = null;
        if (target == GameStateKind.MaybeLater)
        {
            var settings = await settingsRepository.GetAsync(state.ProfileId, ct)
                ?? new ProfileSettings { ProfileId = state.ProfileId };
            until = now.AddDays(settings.MaybeLaterCooldownDays);
        }

        return state with
        {
            State = target,
            ConsecutiveKeepGoingCount = 0,
            MaybeLaterUntilUtc = until,
            StateChangedUtc = now
        };
    }
}

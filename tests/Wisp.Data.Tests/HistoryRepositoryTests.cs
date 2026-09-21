using Wisp.Core.Entities;
using Wisp.Core.Enums;
using Xunit;

namespace Wisp.Data.Tests;

public sealed class HistoryRepositoryTests
{
    [Fact]
    public async Task RecentSessionsAreProfileScopedLimitedAndOrderedByStartThenId()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        var newest = await db.Sessions.StartSessionAsync(new Session
        {
            GameId = game.GameId, ProfileId = profile, StartUtc = TestDatabase.Now.AddHours(1)
        }, default);
        await db.AddSessionAsync(game.GameId, profile);
        var tied = await db.AddSessionAsync(game.GameId, profile);
        await db.AddSessionAsync(game.GameId, other);

        Assert.Equal(new[] { newest, tied }, (await db.Sessions.GetRecentAsync(profile, 2, default))
            .Select(s => s.SessionId));
        Assert.Empty(await db.Sessions.GetRecentAsync(999, 2, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.Sessions.GetRecentAsync(profile, 0, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.Sessions.GetRecentAsync(profile, -1, default));
    }

    [Fact]
    public async Task UpsertKeepsIdentityAndDistinguishesFirstAnswerFromEdits()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var game = await db.AddGameAsync();
        var pending = new Feedback
        {
            SessionId = await db.AddSessionAsync(game.GameId, profile), GameId = game.GameId,
            ProfileId = profile, FeedbackType = FeedbackType.Pending, IsPending = true
        };
        await db.Feedback.RecordAsync(pending, default);
        var id = Assert.Single(await db.Feedback.GetAllAsync(profile, default)).FeedbackId;
        var answer = pending with
        {
            FeedbackType = FeedbackType.KeepGoing, IsPending = false, RecordedUtc = TestDatabase.Now, Edited = true
        };
        await db.Feedback.RecordAsync(answer, default);
        Assert.Equal(answer with { FeedbackId = id, Edited = false },
            Assert.Single(await db.Feedback.GetAllAsync(profile, default)));

        var edit = answer with { FeedbackType = FeedbackType.Finished, RecordedUtc = TestDatabase.Now.AddDays(1), Edited = false };
        await db.Feedback.RecordAsync(edit, default);
        Assert.Equal(edit with { FeedbackId = id, Edited = true },
            Assert.Single(await db.Feedback.GetAllAsync(profile, default)));
    }

    [Fact]
    public async Task RemoveDeletesOnlySelectedFeedbackAndAllIncludesPendingAndAnswered()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        foreach (var owner in new[] { profile, profile, other })
            await db.Feedback.RecordAsync(new Feedback
            {
                SessionId = await db.AddSessionAsync(game.GameId, owner), GameId = game.GameId,
                ProfileId = owner, IsPending = owner == other, FeedbackType = owner == other ? FeedbackType.Pending : FeedbackType.KeepGoing
            }, default);
        var entries = await db.Feedback.GetAllAsync(profile, default);
        Assert.Equal(2, entries.Count);
        var pending = Assert.Single(await db.Feedback.GetAllAsync(other, default));
        Assert.True(pending.IsPending);
        await db.Feedback.RemoveAsync(entries[0].FeedbackId, default);
        await db.Feedback.RemoveAsync(entries[0].FeedbackId, default);
        Assert.Equal(entries[1], Assert.Single(await db.Feedback.GetAllAsync(profile, default)));
        Assert.Equal(pending, Assert.Single(await db.Feedback.GetAllAsync(other, default)));
        Assert.Equal(3, await db.ScalarAsync<int>("SELECT COUNT(*) FROM Sessions;"));
        Assert.Single(await db.Games.GetAllAsync(default));
    }

    [Fact]
    public async Task ClearsCascadeOnlyHistoryAndResetOnlySelectedProfilesStates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var profile = await db.AddProfileAsync();
        var other = await db.AddProfileAsync("other");
        var game = await db.AddGameAsync();
        foreach (var owner in new[] { profile, other })
        {
            var session = await db.AddSessionAsync(game.GameId, owner);
            await db.Sessions.RecordRestartAsync(session, TestDatabase.Now, TestDatabase.Now.AddMinutes(1), default);
            await db.Feedback.RecordAsync(new Feedback
            {
                SessionId = session, GameId = game.GameId, ProfileId = owner, FeedbackType = FeedbackType.KeepGoing
            }, default);
            await db.States.UpsertAsync(new GameState
            {
                GameId = game.GameId, ProfileId = owner, State = GameStateKind.Active,
                ActiveRankScore = 12, ConsecutiveKeepGoingCount = 8, StateChangedUtc = TestDatabase.Now
            }, default);
            await db.Settings.UpsertAsync(new ProfileSettings { ProfileId = owner, MaybeLaterCooldownDays = 19 }, default);
        }
        var otherState = await db.States.GetAsync(game.GameId, other, default);
        var settings = await db.Settings.GetAsync(profile, default);
        await db.Sessions.ClearHistoryAsync(profile, default);
        Assert.NotNull(await db.States.GetAsync(game.GameId, profile, default));
        await db.States.ClearAllAsync(profile, default);
        await db.Sessions.ClearHistoryAsync(profile, default);
        await db.States.ClearAllAsync(profile, default);

        Assert.Empty(await db.Sessions.GetRecentAsync(profile, 10, default));
        Assert.Empty(await db.Feedback.GetAllAsync(profile, default));
        Assert.Null(await db.States.GetAsync(game.GameId, profile, default));
        Assert.Single(await db.Sessions.GetRecentAsync(other, 10, default));
        Assert.Single(await db.Feedback.GetAllAsync(other, default));
        Assert.Equal(otherState, await db.States.GetAsync(game.GameId, other, default));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM SessionRestartEvents;"));
        Assert.Equal(game, Assert.Single(await db.Games.GetAllAsync(default)));
        Assert.Equal(settings, await db.Settings.GetAsync(profile, default));
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ProfileSettings;"));
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM SteamProfiles;"));
    }
}

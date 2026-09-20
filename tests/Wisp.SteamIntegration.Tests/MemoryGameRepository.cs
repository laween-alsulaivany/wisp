using Wisp.Core.Dtos;
using Wisp.Core.Entities;
using Wisp.Core.Interfaces;

namespace Wisp.SteamIntegration.Tests;

internal sealed class MemoryGameRepository(params Game[] games) : IGameRepository
{
    internal Dictionary<long, Game> Rows { get; } = games.ToDictionary(game => game.AppId);
    internal List<Game> Writes { get; } = [];

    public Task UpsertAsync(Game game, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var id = Rows.TryGetValue(game.AppId, out var existing) ? existing.GameId : Rows.Count + 1;
        Rows[game.AppId] = game with { GameId = id };
        Writes.Add(game);
        return Task.CompletedTask;
    }

    public Task<Game?> GetByAppIdAsync(long appId, CancellationToken ct) =>
        Task.FromResult(Rows.GetValueOrDefault(appId));

    public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Game>>(Rows.Values.ToArray());

    public Task<IReadOnlyList<Game>> GetEligiblePoolAsync(int profileId, EligibilityFilter filter, CancellationToken ct) =>
        throw new NotSupportedException("Sync must use the complete library, not the eligibility pool.");
}

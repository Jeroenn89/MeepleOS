using MeepleOS.BusinessLayer.Repositories;

namespace MeepleOS.BusinessLayer.Services;

public sealed record SessionPlayerInput(int PlayerId, int? Score = null, int? Position = null);

public sealed record SessionPlayerResult(int Id, int PlayerId, string PlayerName, int? Score,
    int? Position, bool IsWinner);

public sealed record GameSessionResult(int Id, int BoardGameId, string BoardGameName,
    DateOnly PlayedAt, string? Notes, List<SessionPlayerResult> Players);

public sealed class GameSessionService(IGameSessionRepository sessions)
{
    public async Task<GameSessionResult> RecordAsync(int boardGameId, DateOnly playedAt,
        IReadOnlyList<SessionPlayerInput> players, IReadOnlyCollection<int> winnerPlayerIds, string? notes = null)
    {
        if (boardGameId < 1)
            throw new ArgumentException("Selecteer een geldig bordspel.", nameof(boardGameId));
        if (playedAt == default)
            throw new ArgumentException("Geef een geldige speeldatum op.", nameof(playedAt));
        if (players is null || players.Count == 0)
            throw new ArgumentException("Een potje moet minstens één deelnemer hebben.", nameof(players));
        if (winnerPlayerIds is null || winnerPlayerIds.Count == 0)
            throw new ArgumentException("Selecteer minstens één winnaar.", nameof(winnerPlayerIds));

        var playerIds = new HashSet<int>();
        foreach (var player in players)
        {
            if (player is null || player.PlayerId < 1)
                throw new ArgumentException("Alle deelnemers moeten een geldig speler-ID hebben.", nameof(players));
            if (!playerIds.Add(player.PlayerId))
                throw new ArgumentException("Een speler mag maar één keer aan hetzelfde potje deelnemen.", nameof(players));
            if (player.Position is < 1)
                throw new ArgumentException("Een eindpositie moet minimaal 1 zijn.", nameof(players));
        }

        var winners = new HashSet<int>();
        foreach (var winnerId in winnerPlayerIds)
        {
            if (!winners.Add(winnerId))
                throw new ArgumentException("Een winnaar mag maar één keer opgegeven worden.", nameof(winnerPlayerIds));
            if (!playerIds.Contains(winnerId))
                throw new ArgumentException("Iedere winnaar moet aan het potje deelnemen.", nameof(winnerPlayerIds));
        }

        if (!await sessions.BoardgameExistsAsync(boardGameId))
            throw new ArgumentException("Het bordspel bestaat niet.", nameof(boardGameId));

        var existingPlayerIds = await sessions.GetExistingPlayerIdsAsync(playerIds);
        if (!playerIds.IsSubsetOf(existingPlayerIds))
            throw new ArgumentException("Een of meer deelnemers bestaan niet.", nameof(players));

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        return await sessions.AddAsync(boardGameId, playedAt, notes, players, winners);
    }

    public Task<List<GameSessionResult>> GetAllAsync(int? boardGameId = null, int? playerId = null,
        DateOnly? from = null, DateOnly? to = null)
    {
        if (boardGameId is < 1)
            throw new ArgumentException("Selecteer een geldig bordspel.", nameof(boardGameId));
        if (playerId is < 1)
            throw new ArgumentException("Selecteer een geldige speler.", nameof(playerId));
        if ((from.HasValue && from.Value == default) || (to.HasValue && to.Value == default)
            || (from.HasValue && to.HasValue && from > to))
            throw new ArgumentException("De datumperiode is ongeldig.");

        return sessions.GetAllAsync(boardGameId, playerId, from, to);
    }
}

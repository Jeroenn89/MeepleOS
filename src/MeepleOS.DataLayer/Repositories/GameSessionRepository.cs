using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Repositories;

public sealed class GameSessionRepository(IDbContextFactory<MeepleDbContext> dbFactory) : IGameSessionRepository
{
    public async Task<bool> BoardgameExistsAsync(int boardGameId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Boardgames.AnyAsync(x => x.Id == boardGameId);
    }

    public async Task<HashSet<int>> GetExistingPlayerIdsAsync(IReadOnlyCollection<int> playerIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = playerIds.ToArray();

        return (await db.Players
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync()).ToHashSet();
    }

    public async Task<HashSet<int>> GetExistingExpansionIdsAsync(int boardGameId, IReadOnlyCollection<int> expansionIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = expansionIds.ToArray();
        return (await db.GameExpansions.Where(x => x.BoardGameId == boardGameId && ids.Contains(x.Id))
            .Select(x => x.Id).ToListAsync()).ToHashSet();
    }

    public async Task<GameSessionResult> AddAsync(int boardGameId, DateOnly playedAt, string? notes,
        IReadOnlyList<SessionPlayerInput> players, IReadOnlySet<int> winnerPlayerIds,
        IReadOnlyCollection<int> expansionIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var expansionIdArray = expansionIds.ToArray();
        if (await db.GameExpansions.CountAsync(x => x.BoardGameId == boardGameId && expansionIdArray.Contains(x.Id))
            != expansionIdArray.Length)
            throw new ArgumentException("Een of meer uitbreidingen horen niet bij dit bordspel.", nameof(expansionIds));
        var session = new GameSession
        {
            BoardGameId = boardGameId,
            PlayedAt = playedAt,
            Notes = notes,
            Players = players.Select(x => new SessionPlayer
            {
                PlayerId = x.PlayerId,
                Score = x.Score,
                Position = x.Position,
                IsWinner = winnerPlayerIds.Contains(x.PlayerId)
            }).ToList(),
            Expansions = expansionIds.Select(id => new GameSessionExpansion { GameExpansionId = id }).ToList()
        };

        db.GameSessions.Add(session);
        await db.SaveChangesAsync();

        return await GetByIdAsync(db, session.Id);
    }

    public async Task<List<GameSessionResult>> GetAllAsync(int? boardGameId, int? playerId,
        DateOnly? from, DateOnly? to)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        IQueryable<GameSession> query = db.GameSessions.AsNoTracking();

        if (boardGameId.HasValue)
            query = query.Where(x => x.BoardGameId == boardGameId.Value);

        if (playerId.HasValue)
            query = query.Where(x => x.Players.Any(p => p.PlayerId == playerId.Value));

        if (from.HasValue)
            query = query.Where(x => x.PlayedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.PlayedAt <= to.Value);

        return await query
            .OrderByDescending(x => x.PlayedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new GameSessionResult(x.Id, x.BoardGameId, x.BoardGame.Name,
                x.PlayedAt, x.Notes, x.Players.OrderBy(p => p.Id)
                    .Select(p => new SessionPlayerResult(p.Id, p.PlayerId, p.Player.Name,
                        p.Score, p.Position, p.IsWinner)).ToList(),
                x.Expansions.OrderBy(e => e.GameExpansionId)
                    .Select(e => new GameExpansionResult(e.GameExpansion.Id, e.GameExpansion.BoardGameId,
                        e.GameExpansion.BoardGame.Name, e.GameExpansion.Name, e.GameExpansion.Description)).ToList()))
            .ToListAsync();
    }

    public async Task<GameSessionResult?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GameSessions
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new GameSessionResult(x.Id, x.BoardGameId, x.BoardGame.Name,
                x.PlayedAt, x.Notes, x.Players.OrderBy(p => p.Id)
                    .Select(p => new SessionPlayerResult(p.Id, p.PlayerId, p.Player.Name,
                        p.Score, p.Position, p.IsWinner)).ToList(),
                x.Expansions.OrderBy(e => e.GameExpansionId)
                    .Select(e => new GameExpansionResult(e.GameExpansion.Id, e.GameExpansion.BoardGameId,
                        e.GameExpansion.BoardGame.Name, e.GameExpansion.Name, e.GameExpansion.Description)).ToList()))
            .SingleOrDefaultAsync();
    }

    public async Task<GameSessionResult?> UpdateAsync(int id, int boardGameId, DateOnly playedAt, string? notes,
        IReadOnlyList<SessionPlayerInput> players, IReadOnlySet<int> winnerPlayerIds,
        IReadOnlyCollection<int> expansionIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var session = await db.GameSessions.Include(x => x.Players).Include(x => x.Expansions)
            .SingleOrDefaultAsync(x => x.Id == id);

        if (session is null)
            return null;

        if (!await db.Boardgames.AnyAsync(x => x.Id == boardGameId))
            throw new ArgumentException("Het bordspel bestaat niet.", nameof(boardGameId));

        var ids = players.Select(x => x.PlayerId).ToArray();
        if (await db.Players.CountAsync(x => ids.Contains(x.Id)) != ids.Length)
            throw new ArgumentException("Een of meer deelnemers bestaan niet.", nameof(players));

        var expansionIdArray = expansionIds.ToArray();
        if (await db.GameExpansions.CountAsync(x => x.BoardGameId == boardGameId && expansionIdArray.Contains(x.Id))
            != expansionIdArray.Length)
            throw new ArgumentException("Een of meer uitbreidingen horen niet bij dit bordspel.", nameof(expansionIds));

        session.BoardGameId = boardGameId;
        session.PlayedAt = playedAt;
        session.Notes = notes;
        db.SessionPlayers.RemoveRange(session.Players);
        db.GameSessionExpansions.RemoveRange(session.Expansions);
        await db.SaveChangesAsync();

        session.Players = players.Select(x => new SessionPlayer
        {
            PlayerId = x.PlayerId,
            Score = x.Score,
            Position = x.Position,
            IsWinner = winnerPlayerIds.Contains(x.PlayerId)
        }).ToList();
        session.Expansions = expansionIds.Select(expansionId => new GameSessionExpansion
        {
            GameExpansionId = expansionId
        }).ToList();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(db, id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var session = await db.GameSessions.FindAsync(id);

        if (session is null)
            return false;

        db.GameSessions.Remove(session);
        await db.SaveChangesAsync();

        return true;
    }

    private static async Task<GameSessionResult> GetByIdAsync(MeepleDbContext db, int id) =>
        await db.GameSessions
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new GameSessionResult(x.Id, x.BoardGameId, x.BoardGame.Name,
                x.PlayedAt, x.Notes, x.Players.OrderBy(p => p.Id)
                    .Select(p => new SessionPlayerResult(p.Id, p.PlayerId, p.Player.Name,
                        p.Score, p.Position, p.IsWinner)).ToList(),
                x.Expansions.OrderBy(e => e.GameExpansionId)
                    .Select(e => new GameExpansionResult(e.GameExpansion.Id, e.GameExpansion.BoardGameId,
                        e.GameExpansion.BoardGame.Name, e.GameExpansion.Name, e.GameExpansion.Description)).ToList()))
            .SingleAsync();
}

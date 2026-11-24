using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Repositories;

public sealed class GameExpansionRepository(IDbContextFactory<MeepleDbContext> dbFactory, IAccountContext account) : IGameExpansionRepository
{
    public async Task<GameExpansionResult> AddAsync(int boardGameId, string name, string normalizedName, string? description)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.AccountBoardgames.AnyAsync(x => x.AccountId == accountId && x.BoardGameId == boardGameId))
            throw new ArgumentException("Het bordspel is niet beschikbaar.", nameof(boardGameId));
        if (await db.AccountBoardgames.AnyAsync(x => x.AccountId != accountId && x.BoardGameId == boardGameId))
            throw new InvalidOperationException("Dit bordspel is gedeeld; uitbreidingen kunnen niet rechtstreeks worden gewijzigd.");
        var expansion = new GameExpansion
        {
            BoardGameId = boardGameId, Name = name, NormalizedName = normalizedName, Description = description
        };
        db.GameExpansions.Add(expansion);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            throw new ArgumentException("De uitbreiding kon niet worden toegevoegd; controleer het bordspel en de naam.", nameof(name), ex);
        }
        return (await GetByIdAsync(expansion.Id))!;
    }

    public async Task<List<GameExpansionResult>> GetAllAsync(int? boardGameId)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.GameExpansions.AsNoTracking().Where(x =>
            db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.BoardGameId)
            || db.SessionPlayers.Any(p => p.Player.AccountId == accountId && p.GameSession.BoardGameId == x.BoardGameId));
        if (boardGameId.HasValue)
            query = query.Where(x => x.BoardGameId == boardGameId.Value);
        return await query.OrderBy(x => x.BoardGame.Name).ThenBy(x => x.Name)
            .Select(x => new GameExpansionResult(x.Id, x.BoardGameId, x.BoardGame.Name, x.Name, x.Description))
            .ToListAsync();
    }

    public async Task<GameExpansionResult?> GetByIdAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GameExpansions.AsNoTracking().Where(x => x.Id == id &&
            db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.BoardGameId))
            .Select(x => new GameExpansionResult(x.Id, x.BoardGameId, x.BoardGame.Name, x.Name, x.Description))
            .SingleOrDefaultAsync();
    }

    public async Task<bool> NameExistsAsync(int boardGameId, string normalizedName, int exceptId = 0)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GameExpansions.AnyAsync(x => x.BoardGameId == boardGameId &&
            db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.BoardGameId) &&
            x.NormalizedName == normalizedName && x.Id != exceptId);
    }

    public async Task<GameExpansionResult?> UpdateAsync(int id, string name, string normalizedName, string? description)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var expansion = await db.GameExpansions.SingleOrDefaultAsync(x => x.Id == id &&
            db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.BoardGameId));
        if (expansion is null) return null;
        if (await db.AccountBoardgames.AnyAsync(x => x.BoardGameId == expansion.BoardGameId && x.AccountId != accountId))
            throw new InvalidOperationException("Deze uitbreiding hoort bij een gedeeld bordspel.");
        expansion.Name = name;
        expansion.NormalizedName = normalizedName;
        expansion.Description = description;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            throw new ArgumentException("Deze uitbreiding bestaat al bij dit bordspel.", nameof(name), ex);
        }
        return await GetByIdAsync(id);
    }

    public async Task<bool> HasSessionsAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.GameExpansions.AnyAsync(x => x.Id == id &&
            db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.BoardGameId))) return false;
        return await db.GameSessionExpansions.AnyAsync(x => x.GameExpansionId == id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var expansion = await db.GameExpansions.SingleOrDefaultAsync(x => x.Id == id &&
            db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.BoardGameId));
        if (expansion is null) return false;
        if (await db.AccountBoardgames.AnyAsync(x => x.BoardGameId == expansion.BoardGameId && x.AccountId != accountId))
            throw new InvalidOperationException("Deze uitbreiding hoort bij een gedeeld bordspel.");
        if (await db.GameSessionExpansions.AnyAsync(x => x.GameExpansionId == id))
            throw new InvalidOperationException("Deze uitbreiding kan niet worden verwijderd: ze is gebruikt in gespeelde potjes.");
        db.GameExpansions.Remove(expansion);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException("Deze uitbreiding kan niet worden verwijderd: ze is gebruikt in gespeelde potjes.", ex);
        }
        return true;
    }
}

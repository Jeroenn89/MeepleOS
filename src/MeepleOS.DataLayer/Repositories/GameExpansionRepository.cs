using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Repositories;

public sealed class GameExpansionRepository(IDbContextFactory<MeepleDbContext> dbFactory) : IGameExpansionRepository
{
    public async Task<GameExpansionResult> AddAsync(int boardGameId, string name, string normalizedName, string? description)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
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
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.GameExpansions.AsNoTracking().AsQueryable();
        if (boardGameId.HasValue)
            query = query.Where(x => x.BoardGameId == boardGameId.Value);
        return await query.OrderBy(x => x.BoardGame.Name).ThenBy(x => x.Name)
            .Select(x => new GameExpansionResult(x.Id, x.BoardGameId, x.BoardGame.Name, x.Name, x.Description))
            .ToListAsync();
    }

    public async Task<GameExpansionResult?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GameExpansions.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new GameExpansionResult(x.Id, x.BoardGameId, x.BoardGame.Name, x.Name, x.Description))
            .SingleOrDefaultAsync();
    }

    public async Task<bool> NameExistsAsync(int boardGameId, string normalizedName, int exceptId = 0)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GameExpansions.AnyAsync(x => x.BoardGameId == boardGameId &&
            x.NormalizedName == normalizedName && x.Id != exceptId);
    }

    public async Task<GameExpansionResult?> UpdateAsync(int id, string name, string normalizedName, string? description)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var expansion = await db.GameExpansions.FindAsync(id);
        if (expansion is null) return null;
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
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GameSessionExpansions.AnyAsync(x => x.GameExpansionId == id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var expansion = await db.GameExpansions.FindAsync(id);
        if (expansion is null) return false;
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

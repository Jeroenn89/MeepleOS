using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Repositories;

public sealed class PlayerRepository(IDbContextFactory<MeepleDbContext> dbFactory) : IPlayerRepository
{
    public async Task<PlayerResult> AddOrGetAsync(string name, string normalizedName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.NormalizedName == normalizedName);
        if (existing is not null)
            return new(existing.Id, existing.Name);

        var player = new Player { Name = name, NormalizedName = normalizedName };
        db.Players.Add(player);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // De unieke database-index vangt gelijktijdige registraties af.
            db.Entry(player).State = EntityState.Detached;
            existing = await db.Players.AsNoTracking().SingleAsync(x => x.NormalizedName == normalizedName);
            return new(existing.Id, existing.Name);
        }

        return new(player.Id, player.Name);
    }

    public async Task<List<PlayerResult>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Players
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new PlayerResult(x.Id, x.Name))
            .ToListAsync();
    }

    public async Task<PlayerResult?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Players
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new PlayerResult(x.Id, x.Name))
            .SingleOrDefaultAsync();
    }

    public async Task<bool> NameExistsAsync(string normalizedName, int exceptId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Players.AnyAsync(x => x.NormalizedName == normalizedName && x.Id != exceptId);
    }

    public async Task<PlayerResult?> UpdateAsync(int id, string name, string normalizedName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.FindAsync(id);
        if (player is null)
            return null;

        if (await db.Players.AnyAsync(x => x.NormalizedName == normalizedName && x.Id != id))
            throw new ArgumentException("Er bestaat al een speler met deze naam.", nameof(name));

        player.Name = name;
        player.NormalizedName = normalizedName;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            throw new ArgumentException("Er bestaat al een speler met deze naam.", nameof(name), ex);
        }

        return new(player.Id, player.Name);
    }

    public async Task<bool> HasSessionsAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SessionPlayers.AnyAsync(x => x.PlayerId == id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.FindAsync(id);
        if (player is null)
            return false;

        if (await db.SessionPlayers.AnyAsync(x => x.PlayerId == id))
            throw new InvalidOperationException("Deze speler kan niet worden verwijderd: de speler komt voor in gespeelde potjes.");

        db.Players.Remove(player);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException("Deze speler kan niet worden verwijderd: de speler komt voor in gespeelde potjes.", ex);
        }

        return true;
    }
}

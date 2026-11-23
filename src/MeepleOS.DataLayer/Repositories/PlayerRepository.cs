using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Repositories;

public sealed class PlayerRepository(IDbContextFactory<MeepleDbContext> dbFactory, IAccountContext account) : IPlayerRepository
{
    public async Task<PlayerResult> AddOrGetAsync(string name, string normalizedName)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.NormalizedName == normalizedName &&
                db.AccountPlayers.Any(a => a.AccountId == accountId && a.PlayerId == x.Id));
        if (existing is not null)
            return new(existing.Id, existing.Name);

        var player = new Player { Name = name, NormalizedName = normalizedName };
        db.Players.Add(player);
        db.AccountPlayers.Add(new AccountPlayer { AccountId = accountId, Player = player });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) { throw new InvalidOperationException("De speler kon niet worden opgeslagen.", ex); }

        return new(player.Id, player.Name);
    }

    public async Task<List<PlayerResult>> GetAllAsync()
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Players
            .AsNoTracking()
            .Where(x => db.AccountPlayers.Any(a => a.AccountId == accountId && a.PlayerId == x.Id))
            .OrderBy(x => x.NormalizedName)
            .Select(x => new PlayerResult(x.Id, x.Name))
            .ToListAsync();
    }

    public async Task<PlayerResult?> GetByIdAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Players
            .AsNoTracking()
            .Where(x => x.Id == id && db.AccountPlayers.Any(a => a.AccountId == accountId && a.PlayerId == x.Id))
            .Select(x => new PlayerResult(x.Id, x.Name))
            .SingleOrDefaultAsync();
    }

    public async Task<bool> NameExistsAsync(string normalizedName, int exceptId)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Players.AnyAsync(x => x.NormalizedName == normalizedName && x.Id != exceptId &&
            db.AccountPlayers.Any(a => a.AccountId == accountId && a.PlayerId == x.Id));
    }

    public async Task<PlayerResult?> UpdateAsync(int id, string name, string normalizedName)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.SingleOrDefaultAsync(x => x.Id == id &&
            db.AccountPlayers.Any(a => a.AccountId == accountId && a.PlayerId == x.Id));
        if (player is null)
            return null;
        if ((player.AccountId is not null && player.AccountId != accountId) ||
            (player.AccountId is null && await db.AccountPlayers.AnyAsync(x => x.PlayerId == id && x.AccountId != accountId)))
            throw new InvalidOperationException("Deze speler is gekoppeld aan een ander account.");

        if (await db.Players.AnyAsync(x => x.NormalizedName == normalizedName && x.Id != id &&
            db.AccountPlayers.Any(a => a.AccountId == accountId && a.PlayerId == x.Id)))
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
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.AccountPlayers.AnyAsync(x => x.AccountId == accountId && x.PlayerId == id)) return false;
        return await db.SessionPlayers.AnyAsync(x => x.PlayerId == id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Players.SingleOrDefaultAsync(x => x.Id == id &&
            db.AccountPlayers.Any(a => a.AccountId == accountId && a.PlayerId == x.Id));
        if (player is null)
            return false;

        if (await db.SessionPlayers.AnyAsync(x => x.PlayerId == id))
            throw new InvalidOperationException("Deze speler kan niet worden verwijderd: de speler komt voor in gespeelde potjes.");

        db.AccountPlayers.Remove(await db.AccountPlayers.SingleAsync(x => x.AccountId == accountId && x.PlayerId == id));
        if (player.AccountId == accountId) player.AccountId = null;
        if (!await db.AccountPlayers.AnyAsync(x => x.PlayerId == id && x.AccountId != accountId))
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

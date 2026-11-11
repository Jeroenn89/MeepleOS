using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Repositories;

public sealed class PlayerRepository(IDbContextFactory<MeepleDbContext> dbFactory) : IPlayerRepository
{
    public async Task<PlayerResult> AddOrGetAsync(string name, string normalizedName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.NormalizedName == normalizedName);
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
        return await db.Players.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new PlayerResult(x.Id, x.Name)).ToListAsync();
    }
}

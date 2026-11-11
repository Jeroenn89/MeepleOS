using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Repositories;

public sealed class BoardgameRepository(IDbContextFactory<MeepleDbContext> dbFactory) : IBoardgameRepository
{
    public async Task<BoardgameResult> AddAsync(string name, int minPlayers, int maxPlayers, int estimatedMinutes)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var game = new Boardgame
        {
            Name = name,
            MinPlayers = minPlayers,
            MaxPlayers = maxPlayers,
            EstimatedMinutes = estimatedMinutes
        };
        db.Boardgames.Add(game);
        await db.SaveChangesAsync();
        return new(game.Id, game.Name, game.MinPlayers, game.MaxPlayers, game.EstimatedMinutes);
    }

    public async Task<List<BoardgameResult>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Boardgames.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new BoardgameResult(x.Id, x.Name, x.MinPlayers, x.MaxPlayers, x.EstimatedMinutes))
            .ToListAsync();
    }
}

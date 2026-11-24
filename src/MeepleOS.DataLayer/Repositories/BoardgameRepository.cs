using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.DataLayer.Repositories;

public sealed class BoardgameRepository(IDbContextFactory<MeepleDbContext> dbFactory, IAccountContext account) : IBoardgameRepository
{
    public async Task<BoardgameResult> AddAsync(string name, int minPlayers, int? maxPlayers, int estimatedMinutes)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var game = await db.Boardgames.FirstOrDefaultAsync(x => EF.Functions.Collate(x.Name, "NOCASE") == name);
        if (game is null)
        {
            game = new Boardgame { Name = name, MinPlayers = minPlayers, MaxPlayers = maxPlayers, EstimatedMinutes = estimatedMinutes };
            db.Boardgames.Add(game);
        }
        if (!await db.AccountBoardgames.AnyAsync(x => x.AccountId == accountId && x.BoardGameId == game.Id))
            db.AccountBoardgames.Add(new AccountBoardgame { AccountId = accountId, BoardGame = game });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new(game.Id, game.Name, game.MinPlayers, game.MaxPlayers, game.EstimatedMinutes);
    }

    public async Task<List<BoardgameResult>> GetAllAsync()
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Boardgames
            .AsNoTracking()
            .Where(x => db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id)
                || db.SessionPlayers.Any(p => p.Player.AccountId == accountId && p.GameSession.BoardGameId == x.Id))
            .OrderBy(x => x.Name)
            .Select(x => new BoardgameResult(x.Id, x.Name, x.MinPlayers, x.MaxPlayers, x.EstimatedMinutes,
                db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id)))
            .ToListAsync();
    }

    public async Task<List<BoardgameResult>> GetCollectionAsync()
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Boardgames.AsNoTracking()
            .Where(x => db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id))
            .OrderBy(x => x.Name)
            .Select(x => new BoardgameResult(x.Id, x.Name, x.MinPlayers, x.MaxPlayers, x.EstimatedMinutes))
            .ToListAsync();
    }

    public async Task<BoardgameResult?> FindInCatalogAsync(string name)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var matches = await db.Boardgames.AsNoTracking()
            .Where(x => EF.Functions.Collate(x.Name, "NOCASE") == name)
            .Select(x => new BoardgameResult(x.Id, x.Name, x.MinPlayers, x.MaxPlayers, x.EstimatedMinutes,
                db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id)))
            .Take(2).ToListAsync();
        if (matches.Count > 1)
            throw new InvalidOperationException($"Er staan meerdere bordspellen met de titel {name} in de catalogus.");
        return matches.SingleOrDefault();
    }

    public async Task<BoardgameResult?> AddToCollectionAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var game = await db.Boardgames.SingleOrDefaultAsync(x => x.Id == id &&
            (db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id)
                || db.SessionPlayers.Any(p => p.Player.AccountId == accountId && p.GameSession.BoardGameId == x.Id)));
        if (game is null) return null;
        if (!await db.AccountBoardgames.AnyAsync(x => x.AccountId == accountId && x.BoardGameId == id))
        {
            db.AccountBoardgames.Add(new AccountBoardgame { AccountId = accountId, BoardGameId = id });
            await db.SaveChangesAsync();
        }
        return new(game.Id, game.Name, game.MinPlayers, game.MaxPlayers, game.EstimatedMinutes);
    }

    public async Task<BoardgameResult?> GetByIdAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Boardgames
            .AsNoTracking()
            .Where(x => x.Id == id && (db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id)
                || db.SessionPlayers.Any(p => p.Player.AccountId == accountId && p.GameSession.BoardGameId == x.Id)))
            .Select(x => new BoardgameResult(x.Id, x.Name, x.MinPlayers, x.MaxPlayers, x.EstimatedMinutes,
                db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id)))
            .SingleOrDefaultAsync();
    }

    public async Task<BoardgameResult?> UpdateAsync(int id, string name, int minPlayers, int? maxPlayers, int estimatedMinutes)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var game = await db.Boardgames.SingleOrDefaultAsync(x => x.Id == id &&
            db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id));
        if (game is null)
            return null;
        if (await db.AccountBoardgames.AnyAsync(x => x.BoardGameId == id && x.AccountId != accountId))
            throw new InvalidOperationException("Dit bordspel is gedeeld en kan niet rechtstreeks worden gewijzigd.");

        game.Name = name;
        game.MinPlayers = minPlayers;
        game.MaxPlayers = maxPlayers;
        game.EstimatedMinutes = estimatedMinutes;
        await db.SaveChangesAsync();

        return new(game.Id, game.Name, game.MinPlayers, game.MaxPlayers, game.EstimatedMinutes);
    }

    public async Task<bool> HasSessionsAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.AccountBoardgames.AnyAsync(x => x.AccountId == accountId && x.BoardGameId == id)) return false;
        return await db.GameSessions.AnyAsync(x => x.BoardGameId == id);
    }

    public async Task<bool> HasExpansionsAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.AccountBoardgames.AnyAsync(x => x.AccountId == accountId && x.BoardGameId == id)) return false;
        return await db.GameExpansions.AnyAsync(x => x.BoardGameId == id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var accountId = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var game = await db.Boardgames.SingleOrDefaultAsync(x => x.Id == id &&
            db.AccountBoardgames.Any(a => a.AccountId == accountId && a.BoardGameId == x.Id));
        if (game is null)
            return false;

        if (await db.GameSessions.AnyAsync(x => x.BoardGameId == id))
            throw new InvalidOperationException("Dit bordspel kan niet worden verwijderd: er zijn gespeelde potjes aan gekoppeld.");

        if (await db.GameExpansions.AnyAsync(x => x.BoardGameId == id))
            throw new InvalidOperationException("Dit bordspel kan niet worden verwijderd: er zijn uitbreidingen aan gekoppeld.");

        var collection = await db.AccountBoardgames.SingleAsync(x => x.AccountId == accountId && x.BoardGameId == id);
        db.AccountBoardgames.Remove(collection);
        if (!await db.AccountBoardgames.AnyAsync(x => x.BoardGameId == id && x.AccountId != accountId))
            db.Boardgames.Remove(game);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException("Dit bordspel kan niet worden verwijderd: er zijn gespeelde potjes of uitbreidingen aan gekoppeld.", ex);
        }

        return true;
    }
}

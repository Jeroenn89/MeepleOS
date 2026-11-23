using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.Web.Services;

public static class LegacyAccountBootstrap
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var userName = Environment.GetEnvironmentVariable("MEEPLEOS_BOOTSTRAP_USERNAME");
        var password = Environment.GetEnvironmentVariable("MEEPLEOS_BOOTSTRAP_PASSWORD");
        if (string.IsNullOrWhiteSpace(userName) && string.IsNullOrWhiteSpace(password)) return;
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Stel beide bootstrap-variabelen in.");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeepleDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<MeepleUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (await db.Users.AnyAsync())
            throw new InvalidOperationException("Bootstrap is alleen toegestaan als er nog geen accounts bestaan. Verwijder de bootstrap-variabelen.");
        var matches = await db.Players.Where(p => p.NormalizedName == "JEROEN").ToListAsync();
        if (matches.Count != 1 || matches[0].AccountId is not null)
            throw new InvalidOperationException("Speler Jeroen moet precies eenmaal en nog ongekoppeld aanwezig zijn.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var account = new MeepleUser { UserName = userName.Trim() };
        var created = await users.CreateAsync(account, password);
        if (!created.Succeeded)
            throw new InvalidOperationException("Account aanmaken mislukt: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        if (!await roles.RoleExistsAsync("Admin"))
            await roles.CreateAsync(new IdentityRole("Admin"));
        var roleResult = await users.AddToRoleAsync(account, "Admin");
        if (!roleResult.Succeeded)
            throw new InvalidOperationException("Beheerdersrol kon niet worden opgeslagen.");

        matches[0].AccountId = account.Id;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO AccountPlayers (AccountId, PlayerId) SELECT {account.Id}, Id FROM Players");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO AccountBoardgames (AccountId, BoardGameId) SELECT {account.Id}, Id FROM Boardgames");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO AccountGameSessions (AccountId, GameSessionId) SELECT {account.Id}, Id FROM GameSessions");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE GameSessions SET CreatedByAccountId = {account.Id} WHERE CreatedByAccountId IS NULL");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}

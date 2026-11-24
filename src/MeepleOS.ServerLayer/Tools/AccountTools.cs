using System.ComponentModel;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer.Tools;

public sealed record MyAccountResult(string Username, int? PlayerId, string? PlayerName);

[McpServerToolType]
public sealed class AccountTools(IDbContextFactory<MeepleDbContext> dbFactory, IAccountContext account)
{
    [McpServerTool(Name = "get_my_account"), Description("Haal de geauthenticeerde accountnaam en gekoppelde speler op. De identiteit komt uitsluitend uit de MCP-authenticatie.")]
    public async Task<MyAccountResult> GetMyAccount()
    {
        var id = await account.GetAccountIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
        var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == id);
        return new(user.UserName ?? "", player?.Id, player?.Name);
    }
}

using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.ServerLayer;

public sealed class McpAccountContext(string token, ITimeLimitedDataProtector protector,
    IDbContextFactory<MeepleDbContext> dbFactory) : IAccountContext
{
    public async Task<string> GetAccountIdAsync()
    {
        var payload = McpAuthPayload.Parse(protector.Unprotect(token));
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync(user => user.Id == payload.AccountId && user.SecurityStamp == payload.SecurityStamp))
            throw new UnauthorizedAccessException("De MCP-accounttoken is niet meer geldig.");
        return payload.AccountId;
    }
}

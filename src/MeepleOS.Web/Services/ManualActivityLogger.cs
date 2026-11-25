using System.Text.Json;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.Web.Services;

public sealed class ManualActivityLogger(IDbContextFactory<MeepleDbContext> dbFactory,
    ILogger<ManualActivityLogger> logger)
{
    public async Task LogAsync(string accountId, string action, object? parameters,
        string status = "Success", string? error = null)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var name = await db.Users.AsNoTracking().Where(user => user.Id == accountId)
                .Select(user => user.UserName).SingleOrDefaultAsync() ?? accountId;
            db.ActivityLogs.Add(new ActivityLog
            {
                AccountId = accountId,
                AccountReferenceId = accountId,
                AccountName = name,
                ToolName = "website." + action,
                Parameters = JsonSerializer.Serialize(parameters ?? new { }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Status = status,
                CreatedAt = DateTime.UtcNow,
                ErrorMessage = error
            });
            await db.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning("Handmatige activiteitenregistratie mislukt: {ErrorType}", exception.GetType().Name);
        }
    }
}

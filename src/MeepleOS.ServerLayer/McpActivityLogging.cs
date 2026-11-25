using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer;

public static class McpActivityLogging
{
    // Only the declared game data fields are retained, including fields inside changes and players.
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "minPlayers", "maxPlayers", "estimatedMinutes", "id", "boardGameId",
        "description", "playerId", "players", "playedAt", "winnerPlayerIds", "notes",
        "expansionIds", "scoreWinRule", "score", "position", "changes",
        "expansionIdsToAdd", "expansionIdsToRemove", "from", "to", "year", "month",
        "week", "sortBy", "groupBy"
    };

    public static string SafeParameters(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null) return "{}";
        var safe = new Dictionary<string, object?>();
        foreach (var (key, value) in arguments)
            if (Allowed.Contains(key)) safe[key] = Filter(value);
        return JsonSerializer.Serialize(safe);
    }

    private static object? Filter(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject()
            .Where(p => Allowed.Contains(p.Name))
            .ToDictionary(p => p.Name, p => Filter(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Filter).ToArray(),
        _ => value.Clone()
    };

    public static void Register(IMcpServerBuilder mcp, Func<IServiceProvider> getServices, string databasePath)
    {
        Guid? requestId = Guid.TryParse(Environment.GetEnvironmentVariable("MEEPLEOS_ACTIVITY_REQUEST_ID"), out var id)
            && id != Guid.Empty ? id : null;
        var writeOptions = new DbContextOptionsBuilder<MeepleDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite }.ToString()).Options;
        mcp.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            var services = getServices();
            var factory = services.GetRequiredService<IDbContextFactory<MeepleDbContext>>();
            var accountId = await services.GetRequiredService<IAccountContext>().GetAccountIdAsync();
            var started = DateTime.UtcNow;
            var accountName = accountId;
            try
            {
                await using var db = await factory.CreateDbContextAsync(cancellationToken);
                accountName = await db.Users.Where(u => u.Id == accountId)
                    .Select(u => u.UserName).SingleOrDefaultAsync(cancellationToken) ?? accountId;
            }
            catch (Exception ex)
            {
                services.GetRequiredService<ILoggerFactory>().CreateLogger("McpActivityLogging")
                    .LogWarning("MCP account name lookup failed: {ErrorType}", ex.GetType().Name);
            }

            var toolName = context.Params.Name;
            string parameters;
            try { parameters = SafeParameters(context.Params.Arguments); }
            catch { parameters = "{}"; }

            async Task SaveAsync(string status, string? error)
            {
                try
                {
                    await using var db = new MeepleDbContext(writeOptions);
                    db.ActivityLogs.Add(new ActivityLog
                    {
                        AccountId = accountId, AccountReferenceId = accountId, AccountName = accountName,
                        RequestId = requestId,
                        ToolName = toolName, Parameters = parameters, Status = status,
                        CreatedAt = started, ErrorMessage = error
                    });
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    services.GetRequiredService<ILoggerFactory>().CreateLogger("McpActivityLogging")
                        .LogWarning("MCP activity logging failed: {ErrorType}", ex.GetType().Name);
                }
            }

            try
            {
                var result = await next(context, cancellationToken);
                await SaveAsync(result.IsError == true ? "Failed" : "Success",
                    result.IsError == true ? "De MCP-tool meldde een fout." : null);
                return result;
            }
            catch (Exception ex)
            {
                await SaveAsync("Failed", ex.GetType().Name);
                throw;
            }
        }));
    }
}

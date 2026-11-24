using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Repositories;
using MeepleOS.ServerLayer.Tools;
using MeepleOS.ServerLayer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Logging: de standaarduitvoer blijft vrij voor het MCP-protocol.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Database: bepaal het pad en registreer de SQLite-context.
var databasePath = Environment.GetEnvironmentVariable("MEEPLEOS_DB_PATH")
    ?? Path.Combine(Environment.CurrentDirectory, "data", "meepleos.db");
var readOnly = Environment.GetEnvironmentVariable("MEEPLEOS_READ_ONLY") == "1";
databasePath = Path.GetFullPath(databasePath);
var authToken = Environment.GetEnvironmentVariable("MEEPLEOS_AUTH_TOKEN");
if (string.IsNullOrWhiteSpace(authToken))
    throw new InvalidOperationException("MCP vereist een ingelogde accounttoken.");
var keysPath = databasePath + ".auth-keys";
var tokenProtector = DataProtectionProvider.Create(new DirectoryInfo(keysPath), builder => builder.SetApplicationName("MeepleOS"))
    .CreateProtector("MeepleOS.McpAccount.v1").ToTimeLimitedDataProtector();
var payload = McpAuthPayload.Parse(tokenProtector.Unprotect(authToken));
var accountId = payload.AccountId;
builder.Services.AddSingleton<IAccountContext>(services => new McpAccountContext(authToken, tokenProtector,
    services.GetRequiredService<IDbContextFactory<MeepleDbContext>>()));
if (!readOnly) Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
var connection = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate };
builder.Services.AddDbContextFactory<MeepleDbContext>(options => options.UseSqlite(connection.ToString()));

// Bordspellen: opslag en businesslogica.
builder.Services.AddSingleton<IBoardgameRepository, BoardgameRepository>();
builder.Services.AddSingleton<BoardgameService>();

builder.Services.AddSingleton<IGameExpansionRepository, GameExpansionRepository>();
builder.Services.AddSingleton<GameExpansionService>();

// Spelers: opslag en businesslogica.
builder.Services.AddSingleton<IPlayerRepository, PlayerRepository>();
builder.Services.AddSingleton<PlayerService>();

// Gespeelde potjes: opslag en businesslogica.
builder.Services.AddSingleton<IGameSessionRepository, GameSessionRepository>();
builder.Services.AddSingleton<GameSessionService>();

// Statistieken worden op aanvraag uit geregistreerde potjes berekend.
builder.Services.AddSingleton<StatisticsService>();

// MCP-commando's per categorie.
var mcp = builder.Services.AddMcpServer().WithStdioServerTransport();
mcp.WithTools<BoardgameTools>(); // bordspellen toevoegen, bekijken, wijzigen en verwijderen
mcp.WithTools<AccountTools>();
mcp.WithTools<GameExpansionTools>(); // uitbreidingen beheren
mcp.WithTools<PlayerTools>();    // spelers toevoegen, bekijken, wijzigen en verwijderen
mcp.WithTools<GameSessionTools>(); // potjes registreren, bekijken, wijzigen en verwijderen
mcp.WithTools<StatisticsTools>(); // vier statistiektools

// Start: maak de database aan of voer openstaande schema-upgrades uit.
using var host = builder.Build();
await using (var identityDb = await host.Services.GetRequiredService<IDbContextFactory<MeepleDbContext>>().CreateDbContextAsync())
{
    if (!await identityDb.Users.AnyAsync(x => x.Id == accountId && x.SecurityStamp == payload.SecurityStamp))
        throw new UnauthorizedAccessException("Het MCP-account bestaat niet meer.");
}
if (!readOnly)
{
    await using var db = await host.Services.GetRequiredService<IDbContextFactory<MeepleDbContext>>().CreateDbContextAsync();
    if ((await db.Database.GetPendingMigrationsAsync()).Any())
        await db.Database.MigrateAsync();
}
await host.RunAsync();

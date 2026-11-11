using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data;
using MeepleOS.DataLayer.Repositories;
using MeepleOS.ServerLayer.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Logging: de standaarduitvoer blijft vrij voor het MCP-protocol.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Database: bepaal het pad en registreer de SQLite-context.
var databasePath = Environment.GetEnvironmentVariable("MEEPLEOS_DB_PATH")
    ?? Path.Combine(Environment.CurrentDirectory, "data", "meepleos.db");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
builder.Services.AddDbContextFactory<MeepleDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

// Bordspellen: opslag en businesslogica.
builder.Services.AddSingleton<IBoardgameRepository, BoardgameRepository>();
builder.Services.AddSingleton<BoardgameService>();

// Spelers: opslag en businesslogica.
builder.Services.AddSingleton<IPlayerRepository, PlayerRepository>();
builder.Services.AddSingleton<PlayerService>();

// MCP-commando's per categorie.
var mcp = builder.Services.AddMcpServer().WithStdioServerTransport();
mcp.WithTools<BoardgameTools>(); // add_boardgame, get_boardgames
mcp.WithTools<PlayerTools>();    // add_player, get_players

// Start: maak de database aan als die nog niet bestaat en start daarna de server.
using var host = builder.Build();
await using (var db = await host.Services.GetRequiredService<IDbContextFactory<MeepleDbContext>>().CreateDbContextAsync())
    await db.Database.EnsureCreatedAsync();
await host.RunAsync();

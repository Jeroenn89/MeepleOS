using System.Text;
using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Repositories;
using MeepleOS.Web.Components;
using MeepleOS.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dbPath = Environment.GetEnvironmentVariable("MEEPLEOS_DB_PATH");
if (string.IsNullOrWhiteSpace(dbPath))
    throw new InvalidOperationException("Stel MEEPLEOS_DB_PATH expliciet in voor de website.");
dbPath = Path.GetFullPath(dbPath);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("MEEPLEOS_ROOT")
    ?? Path.Combine(builder.Environment.ContentRootPath, "..", ".."));
builder.Services.AddSingleton(new WebOptions(root, dbPath));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var connection = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
builder.Services.AddDbContextFactory<MeepleDbContext>(options => options.UseSqlite(connection, sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.AddSingleton<IBoardgameRepository, BoardgameRepository>();
builder.Services.AddSingleton<IGameExpansionRepository, GameExpansionRepository>();
builder.Services.AddSingleton<IPlayerRepository, PlayerRepository>();
builder.Services.AddSingleton<IGameSessionRepository, GameSessionRepository>();
builder.Services.AddSingleton<BoardgameService>();
builder.Services.AddSingleton<GameExpansionService>();
builder.Services.AddSingleton<PlayerService>();
builder.Services.AddSingleton<GameSessionService>();
builder.Services.AddSingleton<StatisticsService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddHttpClient();
var app = builder.Build();
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<MeepleDbContext>>().CreateDbContextAsync())
    await db.Database.MigrateAsync();

var urls = (builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5000").Split(';');
var exposed = urls.Any(url => Uri.TryCreate(url.Replace("*", "0.0.0.0").Replace("+", "0.0.0.0"), UriKind.Absolute, out var uri)
    && uri.Host is not ("localhost" or "127.0.0.1" or "[::1]" or "::1"));
var password = Environment.GetEnvironmentVariable("MEEPLEOS_WEB_PASSWORD");
if (exposed && string.IsNullOrWhiteSpace(password))
    throw new InvalidOperationException("Stel MEEPLEOS_WEB_PASSWORD in voordat de website op het netwerk luistert.");
if (exposed)
{
    app.Use(async (context, next) =>
    {
        var header = context.Request.Headers.Authorization.ToString();
        var valid = false;
        if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var value = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..]));
                var separator = value.IndexOf(':');
                if (separator < 0) throw new FormatException();
                var supplied = value[(separator + 1)..];
                valid = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(password!));
            }
            catch (FormatException) { }
        }
        if (!valid)
        {
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"MeepleOS\"";
            context.Response.StatusCode = 401;
            return;
        }
        await next(context);
    });
}
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

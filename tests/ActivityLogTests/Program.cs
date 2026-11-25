using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MeepleOS.AiClient;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using MeepleOS.ServerLayer;
using MeepleOS.DataLayer.Repositories;
using MeepleOS.Web.Components.Pages;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

var root = Directory.GetCurrentDirectory();
var path = Path.Combine(Path.GetTempPath(), $"meepleos-activity-{Guid.NewGuid():N}.db");
var options = new DbContextOptionsBuilder<MeepleDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
try
{
    await using (var db = new MeepleDbContext(options))
    {
        await db.Database.MigrateAsync();
        var hasher = new PasswordHasher<MeepleUser>();
        var jeroen = new MeepleUser { Id = "jeroen-test", UserName = "Jeroen", NormalizedUserName = "JEROEN", SecurityStamp = "jeroen-stamp" };
        var wim = new MeepleUser { Id = "wim-test", UserName = "Wim", NormalizedUserName = "WIM", SecurityStamp = "wim-stamp" };
        jeroen.PasswordHash = hasher.HashPassword(jeroen, "TestPassword123!");
        wim.PasswordHash = hasher.HashPassword(wim, "TestPassword123!");
        db.Users.AddRange(jeroen, wim);
        db.Roles.Add(new IdentityRole { Id = "admin-test", Name = "Admin", NormalizedName = "ADMIN" });
        db.UserRoles.Add(new IdentityUserRole<string> { RoleId = "admin-test", UserId = jeroen.Id });
        await db.SaveChangesAsync();
    }
    var protector = DataProtectionProvider.Create(new DirectoryInfo(path + ".auth-keys"),
        builder => builder.SetApplicationName("MeepleOS"))
        .CreateProtector("MeepleOS.McpAccount.v1").ToTimeLimitedDataProtector();
    string Token(string id, string stamp) => protector.Protect(new McpAuthPayload(id, stamp).Serialize(), TimeSpan.FromMinutes(10));

    int gameId, jeroenId, wimId;
    var requestId = Guid.NewGuid();
    await using (var jeroen = await McpTools.ConnectAsync(root, databasePath: path,
        authToken: Token("jeroen-test", "jeroen-stamp"), requestId: requestId))
    {
        Check((await jeroen.CallAsync("get_boardgames")).GetArrayLength() == 0, "parameterloze leesaanroep");
        gameId = (await jeroen.CallAsync("add_boardgame", new { name = "Activiteitsspel", minPlayers = 2, maxPlayers = 4, estimatedMinutes = 30 })).GetProperty("id").GetInt32();
        jeroenId = (await jeroen.CallAsync("add_player", new { name = "Jeroen" })).GetProperty("id").GetInt32();
        wimId = (await jeroen.CallAsync("add_player", new { name = "Wim" })).GetProperty("id").GetInt32();
        await jeroen.CallAsync("record_game_session", new
        {
            boardGameId = gameId, playedAt = "2026-11-25",
            players = new[] { new { playerId = jeroenId, score = 40, position = 1 }, new { playerId = wimId, score = 20, position = 2 } },
            winnerPlayerIds = new[] { jeroenId }
        });
        await jeroen.CallAsync("record_game_session", new
        {
            boardGameId = gameId, playedAt = "2026-11-24",
            players = new[] { new { playerId = jeroenId }, new { playerId = wimId } },
            winnerPlayerIds = new[] { wimId }
        });
        try
        {
            await jeroen.CallAsync("add_boardgame", new { name = "Ongeldig", minPlayers = 0, maxPlayers = 1, estimatedMinutes = 10 });
            throw new Exception("Mislukte toolaanroep slaagde onverwacht.");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("MCP add_boardgame:")) { }
    }
    await using (var jeroen = await McpTools.ConnectAsync(root, databasePath: path, authToken: Token("jeroen-test", "jeroen-stamp")))
        for (var i = 0; i < 21; i++) await jeroen.CallAsync("get_boardgames");
    await using (var wim = await McpTools.ConnectAsync(root, readOnly: true, databasePath: path, authToken: Token("wim-test", "wim-stamp")))
        await wim.CallAsync("get_boardgames");

    await CheckManualManageAsync(options);
    var webDll = args.SingleOrDefault()
        ?? Path.Combine(root, "src", "MeepleOS.Web", "bin", "Debug", "net10.0", "MeepleOS.Web.dll");
    await CheckAdminPageAsync(root, path, webDll);

    await using (var db = new MeepleDbContext(options))
    {
        var logs = await db.ActivityLogs.OrderBy(x => x.Id).ToListAsync();
        Check(logs.Count >= 8, "alle toolaanroepen geregistreerd");
        Check(logs.Count(x => x.RequestId == requestId) == 7
            && logs.Count(x => x.RequestId is null && !x.ToolName.StartsWith("website.")) == 22,
            "alleen aanroepen uit hetzelfde chatverzoek delen een request-ID");
        Check(logs.Count(x => x.ToolName is "website.add_boardgame" or "website.update_boardgame") == 2
            && logs.Any(x => x.ToolName == "website.create_account" && x.AccountName == "Jeroen")
            && logs.Any(x => x.ToolName == "website.reset_password" && x.AccountName == "Jeroen")
            && logs.Where(x => x.ToolName.StartsWith("website.")).All(x => !x.Parameters.Contains("TestPassword123!")),
            "handmatige wijzigingen en accountbeheer zonder wachtwoorden gelogd");
        Check(logs.Any(x => x.AccountId == "jeroen-test" && x.AccountName == "Jeroen"), "Jeroen als aanroeper");
        Check(logs.Any(x => x.AccountId == "wim-test" && x.AccountName == "Wim" && x.ToolName == "get_boardgames"), "Wim in alleen-lezenstand");
        Check(logs.Any(x => x.ToolName == "get_boardgames" && x.Parameters == "{}"), "lege parameters");
        Check(logs.Any(x => x.ToolName == "add_boardgame" && x.Parameters.Contains("Activiteitsspel")), "spelparameters");
        var sessions = logs.Where(x => x.ToolName == "record_game_session").ToList();
        Check(sessions.Count == 2 && sessions.All(x => x.Status == "Success"), "beide potjes");
        Check(sessions.Any(x => x.Parameters.Contains("\"score\":40") && x.Parameters.Contains("winnerPlayerIds")), "scores en winnaar");
        Check(sessions.Any(x => !x.Parameters.Contains("\"score\"")), "potje zonder scores");
        Check(logs.Any(x => x.ToolName == "add_boardgame" && x.Status == "Failed"), "mislukte aanroep");
        Check(logs.All(x => x.CreatedAt.Kind == DateTimeKind.Utc || x.CreatedAt.ToString("O").EndsWith("Z")), "UTC-tijdstempel");
        var tainted = new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Veilig"),
            ["accessToken"] = JsonSerializer.SerializeToElement("GEHEIM"),
            ["changes"] = JsonSerializer.SerializeToElement(new { notes = "Spelnotitie", password = "GEHEIM", players = new[] { new { playerId = 1, score = 2, apiKey = "GEHEIM" } } })
        };
        var filtered = McpActivityLogging.SafeParameters(tainted);
        Check(!filtered.Contains("GEHEIM") && filtered.Contains("Spelnotitie"), "authenticatievelden gefilterd");
        db.Users.Remove(await db.Users.SingleAsync(x => x.Id == "wim-test"));
        await db.SaveChangesAsync();
        var oldLog = await db.ActivityLogs.SingleAsync(x => x.AccountId == "wim-test");
        Check(oldLog.AccountReferenceId is null && oldLog.AccountName == "Wim", "log blijft na accountverwijdering");
    }
    Console.WriteLine("ActivityLogTests: MCP-aanroepen, accounts, fouten, parameters, adminrechten en accountverwijdering geslaagd.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    throw;
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
    var keys = path + ".auth-keys";
    if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static async Task CheckManualManageAsync(DbContextOptions<MeepleDbContext> options)
{
    var factory = new TestFactory(options);
    var account = new TestAccountContext("jeroen-test");
    var page = new Manage();
    Set("GameService", new BoardgameService(new BoardgameRepository(factory, account)));
    Set("ExpansionService", new GameExpansionService(new GameExpansionRepository(factory, account),
        new BoardgameRepository(factory, account)));
    Set("PlayerService", new PlayerService(new PlayerRepository(factory, account)));
    Set("SessionService", new GameSessionService(new GameSessionRepository(factory, account)));
    Set("AccountContext", account);
    Set("ActivityLog", new ManualActivityLogger(factory, NullLogger<ManualActivityLogger>.Instance));
    Field("editGameSearch", "Handmatig spel");
    typeof(Manage).GetMethod("StartNewGame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
    Check((string)ReadField("gameName") == "Handmatig spel"
        && (string)ReadField("editGameSearch") == "" && (int)ReadField("editGameId") == 0,
        "nieuwknop neemt een gezochte titel over");
    Field("minPlayers", 2);
    Field("maxPlayers", (int?)5);
    Field("duration", 60);
    await Save();
    await using (var db = new MeepleDbContext(options))
        Field("editGameId", (await db.Boardgames.SingleAsync(x => x.Name == "Handmatig spel")).Id);
    Field("gameName", "Handmatig spel bijgewerkt");
    Field("duration", 75);
    await Save();
    typeof(Manage).GetMethod("StartNewGame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
    Check((string)ReadField("gameName") == "" && (int)ReadField("editGameId") == 0,
        "nieuwknop verlaat de bewerkmodus");
    await using (var db = new MeepleDbContext(options))
    {
        Check(await db.ActivityLogs.CountAsync(x => x.ToolName == "website.add_boardgame") == 1
            && await db.ActivityLogs.CountAsync(x => x.ToolName == "website.update_boardgame") == 1,
            "handmatig toevoegen en wijzigen gelogd");
        Check(await db.Boardgames.AnyAsync(x => x.Name == "Handmatig spel bijgewerkt" && x.EstimatedMinutes == 75),
            "handmatig bordspel gewijzigd");
    }

    void Set(string name, object value) => typeof(Manage).GetProperty(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(page, value);
    void Field(string name, object value) => typeof(Manage).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    object ReadField(string name) => typeof(Manage).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
    async Task Save() => await (Task)typeof(Manage).GetMethod("SaveGame", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(page, null)!;
}

static async Task CheckAdminPageAsync(string root, string database, string webDll)
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    using var process = new System.Diagnostics.Process
    {
        StartInfo = new("dotnet", $"\"{webDll}\"") { UseShellExecute = false, CreateNoWindow = true }
    };
    process.StartInfo.Environment["MEEPLEOS_DB_PATH"] = database;
    process.StartInfo.Environment["MEEPLEOS_ROOT"] = root;
    process.StartInfo.Environment["ASPNETCORE_URLS"] = $"https://localhost:{port}";
    process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    process.StartInfo.Environment.Remove("MEEPLEOS_BOOTSTRAP_USERNAME");
    process.StartInfo.Environment.Remove("MEEPLEOS_BOOTSTRAP_PASSWORD");
    process.Start();
    try
    {
        HttpClient Client() => new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        }) { BaseAddress = new Uri($"https://localhost:{port}") };
        using var anonymous = Client();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (process.HasExited) throw new Exception("Webserver stopte tijdens de admintest.");
            try { if ((await anonymous.GetAsync("/login")).IsSuccessStatusCode) break; }
            catch (HttpRequestException) { }
            await Task.Delay(250);
        }
        Check((await anonymous.GetAsync("/accounts")).StatusCode != HttpStatusCode.OK, "anoniem heeft geen toegang");
        using var regular = Client();
        await Login(regular, "Wim");
        Check((await regular.GetAsync("/accounts?logStatus=Failed")).StatusCode != HttpStatusCode.OK, "gewone gebruiker heeft geen toegang");
        using var admin = Client();
        await Login(admin, "Jeroen");
        var manage = await admin.GetStringAsync("/manage");
        Check(manage.Contains("Nieuw bordspel opslaan") && !manage.Contains("Nieuw bordspel invoeren"),
            "nieuwknop verschijnt niet wanneer het lege nieuw-formulier al actief is");
        var page = await admin.GetAsync("/accounts?logStatus=Failed");
        var body = await page.Content.ReadAsStringAsync();
        Check(page.StatusCode == HttpStatusCode.OK && body.Contains("MCP-activiteitenlog")
            && body.Contains("Ongeldig") && body.Contains("Bestaande accounts"), "admin ziet log en accountbeheer");
        Check(body.Contains("1 item · Pagina 1 van 1") && body.Contains("Chatverzoek · 7 toolaanroepen")
            && body.Contains("1 mislukt") && body.Contains("record_game_session"),
            "filter op fouten laat het volledige chatverzoek zien");
        var filtered = await admin.GetStringAsync("/accounts?logAccount=wim-test&logTool=get_boardgames&logStatus=Success");
        Check(filtered.Contains("Wim") && filtered.Contains("get_boardgames") && filtered.Contains("1 item")
            && !filtered.Contains("Chatverzoek"), "account- en toolfilter voor losse aanroepen");
        var many = await admin.GetStringAsync("/accounts?logPage=999");
        Check(many.Contains("Pagina 2 van 2") && many.Contains("Vorige")
            && many.Contains("25 items") && many.Contains("Chatverzoek · 7 toolaanroepen"),
            "paginering telt chatverzoeken als één item en houdt de groep bij elkaar");
        var manual = await admin.GetStringAsync("/accounts?logTool=website.update_boardgame");
        Check(manual.Contains("Handmatig spel bijgewerkt") && manual.Contains("Jeroen")
            && manual.Contains("website.update_boardgame"), "handmatige wijzigingen zijn zichtbaar voor admins");
        var accountToken = Regex.Match(body, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var created = await admin.PostAsync("/accounts", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(accountToken),
            ["username"] = "NieuwTestaccount",
            ["password"] = "TestPassword123!",
            ["playerId"] = ""
        }));
        Check(created.StatusCode == HttpStatusCode.Redirect && created.Headers.Location?.ToString() == "/accounts?created=1",
            "bestaande accountaanmaak blijft werken");
        await using (var db = new MeepleDbContext(new DbContextOptionsBuilder<MeepleDbContext>()
            .UseSqlite($"Data Source={database};Pooling=False").Options))
        {
            var newAccountId = await db.Users.Where(x => x.UserName == "NieuwTestaccount")
                .Select(x => x.Id).SingleAsync();
            var reset = await admin.PostAsync($"/accounts/{newAccountId}/reset-password",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = WebUtility.HtmlDecode(accountToken),
                    ["password"] = "TestPassword123!"
                }));
            Check(reset.StatusCode == HttpStatusCode.Redirect && reset.Headers.Location?.ToString() == "/accounts?updated=1",
                "wachtwoordreset blijft werken");
        }

        async Task Login(HttpClient client, string name)
        {
            var login = await client.GetStringAsync("/login");
            var token = Regex.Match(login, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
            Check(token.Length > 0, "antiforgery-token");
            var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
                ["username"] = name,
                ["password"] = "TestPassword123!"
            }));
            Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.ToString() == "/", "inloggen");
        }
    }
    finally
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
}

sealed class TestFactory(DbContextOptions<MeepleDbContext> options) : IDbContextFactory<MeepleDbContext>
{
    public MeepleDbContext CreateDbContext() => new(options);
}

sealed class TestAccountContext(string accountId) : IAccountContext
{
    public Task<string> GetAccountIdAsync() => Task.FromResult(accountId);
}

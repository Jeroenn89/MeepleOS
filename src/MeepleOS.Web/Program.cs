using System.Net;
using System.Text;
using MeepleOS.BusinessLayer.Repositories;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Repositories;
using MeepleOS.DataLayer.Data.Models;
using MeepleOS.Web.Components;
using MeepleOS.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Authorization;

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
builder.Services.AddDataProtection().SetApplicationName("MeepleOS")
    .PersistKeysToFileSystem(new DirectoryInfo(dbPath + ".auth-keys"));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
var connection = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
builder.Services.AddDbContextFactory<MeepleDbContext>(options => options.UseSqlite(connection, sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.AddIdentity<MeepleUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<MeepleDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Admin"))
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build());
builder.Services.AddScoped<IAccountContext, BlazorAccountContext>();
builder.Services.AddScoped<IBoardgameRepository, BoardgameRepository>();
builder.Services.AddScoped<IGameExpansionRepository, GameExpansionRepository>();
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<IGameSessionRepository, GameSessionRepository>();
builder.Services.AddScoped<BoardgameService>();
builder.Services.AddScoped<GameExpansionService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<GameSessionService>();
builder.Services.AddScoped<StatisticsService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<ManualActivityLogger>();
builder.Services.AddHttpClient();
var app = builder.Build();
try
{
    await using var db = await app.Services.GetRequiredService<IDbContextFactory<MeepleDbContext>>().CreateDbContextAsync();
    // A routine start only needs a read. MigrateAsync takes a SQLite write lock even when
    // the schema is current, which conflicts with other local database users.
    if ((await db.Database.GetPendingMigrationsAsync()).Any())
        await db.Database.MigrateAsync();
}
catch (Exception ex)
{
    if ((ex as SqliteException ?? ex.GetBaseException() as SqliteException) is { } sqlite)
        app.Logger.LogError(ex, "SQLite startup error {Code}/{ExtendedCode} for {DatabasePath}",
            sqlite.SqliteErrorCode, sqlite.SqliteExtendedErrorCode, dbPath);
    throw;
}
await LegacyAccountBootstrap.RunAsync(app.Services);

var urls = (builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5000").Split(';');
var exposed = urls.Any(url => Uri.TryCreate(url.Replace("*", "0.0.0.0").Replace("+", "0.0.0.0"), UriKind.Absolute, out var uri)
    && uri.Host is not ("localhost" or "127.0.0.1" or "[::1]" or "::1"));
if (exposed && !urls.All(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Gebruik HTTPS wanneer de website op het netwerk luistert.");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapGet("/login", (HttpContext context, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(context).RequestToken;
    var error = context.Request.Query.ContainsKey("error") ? "<p class=\"auth-message auth-error\" role=\"alert\">Inloggen mislukt.</p>" : "";
    return AuthPage.Render("Inloggen", "Log in om je collectie en potjes te bekijken.", $$"""
        <form method="post" action="/login"><input type="hidden" name="__RequestVerificationToken" value="{{WebUtility.HtmlEncode(token)}}">
        <label>Gebruikersnaam<input name="username" autocomplete="username" required></label>
        <label>Wachtwoord<input name="password" type="password" autocomplete="current-password" required></label>
        <button type="submit">Inloggen</button></form>
        """, error, showBackLink: false);
}).AllowAnonymous();
app.MapPost("/login", async (HttpContext context, IAntiforgery antiforgery, SignInManager<MeepleUser> signIn) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync();
    var userName = form["username"].ToString();
    var password = form["password"].ToString();
    var result = await signIn.PasswordSignInAsync(userName, password, false, lockoutOnFailure: true);
    return Results.Redirect(result.Succeeded ? "/" : "/login?error=1");
}).AllowAnonymous();
app.MapGet("/change-password", (HttpContext context, IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var token = WebUtility.HtmlEncode(antiforgery.GetAndStoreTokens(context).RequestToken);
    var message = context.Request.Query.ContainsKey("changed") ? "<p class=\"auth-message auth-success\" role=\"status\">Je wachtwoord is gewijzigd.</p>" :
        context.Request.Query.ContainsKey("mismatch") ? "<p class=\"auth-message auth-error\" role=\"alert\">De nieuwe wachtwoorden komen niet overeen.</p>" :
        context.Request.Query.ContainsKey("error") ? "<p class=\"auth-message auth-error\" role=\"alert\">Wachtwoord wijzigen mislukt. Controleer je huidige wachtwoord en de eisen voor het nieuwe wachtwoord.</p>" : "";
    return AuthPage.Render("Wachtwoord wijzigen", "Kies een nieuw wachtwoord voor je account.", $$"""
        <form method="post" action="/change-password"><input type="hidden" name="__RequestVerificationToken" value="{{token}}">
        <label>Huidig wachtwoord<input name="currentPassword" type="password" autocomplete="current-password" required></label>
        <label>Nieuw wachtwoord<input name="newPassword" type="password" autocomplete="new-password" minlength="12" required></label>
        <label>Herhaal nieuw wachtwoord<input name="confirmPassword" type="password" autocomplete="new-password" minlength="12" required></label>
        <p class="auth-hint">Minimaal 12 tekens, met een hoofdletter, kleine letter, cijfer en symbool.</p>
        <button type="submit">Wachtwoord wijzigen</button></form>
        """, message);
}).RequireAuthorization();
app.MapPost("/change-password", async (HttpContext context, IAntiforgery antiforgery,
    UserManager<MeepleUser> users, SignInManager<MeepleUser> signIn, ManualActivityLogger activity) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync();
    var current = form["currentPassword"].ToString();
    var next = form["newPassword"].ToString();
    if (next != form["confirmPassword"].ToString())
        return Results.Redirect("/change-password?mismatch=1");
    var user = await users.GetUserAsync(context.User);
    if (user is null) return Results.Challenge();
    var result = await users.ChangePasswordAsync(user, current, next);
    await activity.LogAsync(user.Id, "change_password", new { accountId = user.Id },
        result.Succeeded ? "Success" : "Failed", result.Succeeded ? null : "IdentityValidationFailed");
    if (!result.Succeeded) return Results.Redirect("/change-password?error=1");
    await signIn.RefreshSignInAsync(user);
    return Results.Redirect("/change-password?changed=1");
}).RequireAuthorization();
app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery, SignInManager<MeepleUser> signIn) =>
{
    await antiforgery.ValidateRequestAsync(context);
    await signIn.SignOutAsync();
    return Results.Redirect("/login");
}).RequireAuthorization();
app.MapGet("/mcp-token", async (HttpContext context, IDataProtectionProvider protection,
    UserManager<MeepleUser> users) =>
{
    var user = await users.GetUserAsync(context.User) ?? throw new UnauthorizedAccessException();
    var token = protection.CreateProtector("MeepleOS.McpAccount.v1")
        .ToTimeLimitedDataProtector().Protect(new McpAuthPayload(user.Id, user.SecurityStamp!).Serialize(), TimeSpan.FromMinutes(10));
    context.Response.Headers.CacheControl = "no-store";
    return Results.Text(token);
}).RequireAuthorization();
app.MapGet("/accounts", async (HttpContext context, IAntiforgery antiforgery, MeepleDbContext db,
    UserManager<MeepleUser> userManager) =>
{
    const int pageSize = 20;
    var search = context.Request.Query["q"].ToString().Trim();
    if (search.Length > 200) search = search[..200];
    var requestedPage = int.TryParse(context.Request.Query["page"], out var parsedPage) && parsedPage > 0 ? parsedPage : 1;
    var accountQuery = db.Users.AsNoTracking();
    if (search.Length > 0)
        accountQuery = accountQuery.Where(u => u.UserName != null && u.UserName.Contains(search));
    var accountCount = await accountQuery.CountAsync();
    var pageCount = Math.Max(1, (int)Math.Ceiling(accountCount / (double)pageSize));
    var page = Math.Min(requestedPage, pageCount);
    var accounts = await accountQuery.OrderBy(u => u.UserName).ThenBy(u => u.Id)
        .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    var players = await db.Players.Where(p => p.AccountId == null)
        .OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync();
    var options = string.Join("", players.Select(p => $"<option value=\"{p.Id}\">{WebUtility.HtmlEncode(p.Name)}</option>"));
    var token = WebUtility.HtmlEncode(antiforgery.GetAndStoreTokens(context).RequestToken);
    var accountIds = accounts.Select(u => u.Id).ToList();
    var linked = await db.Players.Where(p => p.AccountId != null && accountIds.Contains(p.AccountId))
        .Select(p => new { p.AccountId, p.Name }).ToDictionaryAsync(p => p.AccountId!);
    var accountRows = new System.Text.StringBuilder();
    foreach (var user in accounts)
    {
        var id = WebUtility.HtmlEncode(user.Id);
        var name = WebUtility.HtmlEncode(user.UserName);
        var playerName = linked.TryGetValue(user.Id, out var player) ? WebUtility.HtmlEncode(player.Name) : "Geen speler gekoppeld";
        var role = await userManager.IsInRoleAsync(user, "Admin") ? "Beheerder" : "Gebruiker";
        accountRows.Append($$"""
            <article><details><summary><span class="account-summary-name">{{name}}</span><span class="account-summary-meta">{{role}} · Speler: {{playerName}}</span></summary><div class="account-actions">
            <form method="post" action="/accounts/{{id}}/rename"><input type="hidden" name="__RequestVerificationToken" value="{{token}}">
            <label>Gebruikersnaam wijzigen<input name="username" value="{{name}}" required></label><button type="submit">Naam opslaan</button></form>
            <form method="post" action="/accounts/{{id}}/reset-password"><input type="hidden" name="__RequestVerificationToken" value="{{token}}">
            <label>Nieuw tijdelijk wachtwoord<input name="password" type="password" autocomplete="new-password" minlength="12" required></label><button type="submit">Wachtwoord opnieuw instellen</button></form>
            {{(player is null && players.Count > 0 ? $$"""
                <form method="post" action="/accounts/{{id}}/link-player"><input type="hidden" name="__RequestVerificationToken" value="{{token}}">
                <label>Speler koppelen<select name="playerId" required><option value="">Kies een speler</option>{{options}}</select></label>
                <button type="submit">Speler koppelen</button></form>
                """ : "")}}
            </div></details></article>
            """);
    }
    var searchValue = WebUtility.HtmlEncode(search);
    string PageUrl(int target) => "/accounts?page=" + target + (search.Length > 0 ? "&q=" + Uri.EscapeDataString(search) : "");
    var firstResult = accountCount == 0 ? 0 : (page - 1) * pageSize + 1;
    var lastResult = Math.Min(page * pageSize, accountCount);
    var pagination = pageCount > 1 ? $$"""
        <nav class="account-pagination" aria-label="Accountpagina's">
            {{(page > 1 ? $"<a href=\"{WebUtility.HtmlEncode(PageUrl(page - 1))}\">Vorige</a>" : "")}}
            <span>Pagina {{page}} van {{pageCount}}</span>
            {{(page < pageCount ? $"<a href=\"{WebUtility.HtmlEncode(PageUrl(page + 1))}\">Volgende</a>" : "")}}
        </nav>
        """ : "";
    var message = context.Request.Query.ContainsKey("created") ? "<p class=\"auth-message auth-success\" role=\"status\">Account aangemaakt.</p>" :
        context.Request.Query.ContainsKey("updated") ? "<p class=\"auth-message auth-success\" role=\"status\">Account bijgewerkt.</p>" :
        context.Request.Query.ContainsKey("error") ? "<p class=\"auth-message auth-error\" role=\"alert\">Accountbewerking mislukt. Controleer de ingevoerde gegevens.</p>" : "";
    var activityLog = await ActivityLogSection.RenderAsync(db, context.Request.Query);
    return AuthPage.Render("Accounts", "Bekijk en beheer de accounts van MeepleOS.", $$"""
        <details class="account-create"><summary>Nieuw account aanmaken</summary>
        <form method="post" action="/accounts"><input type="hidden" name="__RequestVerificationToken" value="{{token}}">
        <label>Gebruikersnaam<input name="username" required></label>
        <label>Tijdelijk wachtwoord<input name="password" type="password" required minlength="12"></label>
        <label>Bestaande speler (optioneel)<select name="playerId"><option value="">Geen koppeling</option>{{options}}</select></label>
        <button type="submit">Account aanmaken</button></form></details>
        <div class="account-list"><h2>Bestaande accounts ({{accountCount}})</h2>
        <form class="account-search" method="get" action="/accounts"><label for="account-query">Zoek op gebruikersnaam</label>
        <div><input id="account-query" type="search" name="q" value="{{searchValue}}" placeholder="Gebruikersnaam"><button type="submit">Zoeken</button></div></form>
        <p class="account-result-count">{{(accountCount == 0 ? "Geen accounts gevonden." : $"Accounts {firstResult}–{lastResult} van {accountCount}")}}</p>
        {{accountRows}}{{pagination}}</div>
        {{activityLog}}
        """, message, wide: true);
}).RequireAuthorization("Admin");
app.MapPost("/accounts", async (HttpContext context, IAntiforgery antiforgery, MeepleDbContext db,
    UserManager<MeepleUser> users, ManualActivityLogger activity) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync();
    var name = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var playerText = form["playerId"].ToString();
    int? playerId = null;
    if (!string.IsNullOrEmpty(playerText))
    {
        if (!int.TryParse(playerText, out var parsed) || !await db.Players.AnyAsync(p => p.Id == parsed && p.AccountId == null))
            return Results.Redirect("/accounts?error=1");
        playerId = parsed;
    }
    await using var transaction = await db.Database.BeginTransactionAsync();
    var user = new MeepleUser { UserName = name };
    var created = await users.CreateAsync(user, password);
    if (!created.Succeeded) return Results.Redirect("/accounts?error=1");
    if (playerId.HasValue)
    {
        var player = await db.Players.SingleAsync(p => p.Id == playerId.Value && p.AccountId == null);
        player.AccountId = user.Id;
        db.AccountPlayers.Add(new() { AccountId = user.Id, PlayerId = player.Id });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO AccountGameSessions (AccountId, GameSessionId) SELECT {user.Id}, GameSessionId FROM SessionPlayers WHERE PlayerId = {player.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO AccountPlayers (AccountId, PlayerId) SELECT {user.Id}, other.PlayerId FROM SessionPlayers own JOIN SessionPlayers other ON other.GameSessionId = own.GameSessionId WHERE own.PlayerId = {player.Id}");
    }
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    await activity.LogAsync(users.GetUserId(context.User)!, "create_account",
        new { accountId = user.Id, username = name, playerId });
    return Results.Redirect("/accounts?created=1");
}).RequireAuthorization("Admin");
app.MapPost("/accounts/{id}/rename", async (string id, HttpContext context, IAntiforgery antiforgery,
    UserManager<MeepleUser> users, ManualActivityLogger activity) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var user = await users.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    var form = await context.Request.ReadFormAsync();
    var name = form["username"].ToString().Trim();
    if (name.Length is < 1 or > 200) return Results.Redirect("/accounts?error=1");
    var result = await users.SetUserNameAsync(user, name);
    await activity.LogAsync(users.GetUserId(context.User)!, "rename_account",
        new { accountId = id, username = name }, result.Succeeded ? "Success" : "Failed",
        result.Succeeded ? null : "IdentityValidationFailed");
    return Results.Redirect(result.Succeeded ? "/accounts?updated=1" : "/accounts?error=1");
}).RequireAuthorization("Admin");
app.MapPost("/accounts/{id}/reset-password", async (string id, HttpContext context, IAntiforgery antiforgery,
    UserManager<MeepleUser> users, ManualActivityLogger activity) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var user = await users.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    var form = await context.Request.ReadFormAsync();
    var token = await users.GeneratePasswordResetTokenAsync(user);
    var result = await users.ResetPasswordAsync(user, token, form["password"].ToString());
    await activity.LogAsync(users.GetUserId(context.User)!, "reset_password",
        new { accountId = id }, result.Succeeded ? "Success" : "Failed",
        result.Succeeded ? null : "IdentityValidationFailed");
    return Results.Redirect(result.Succeeded ? "/accounts?updated=1" : "/accounts?error=1");
}).RequireAuthorization("Admin");
app.MapPost("/accounts/{id}/link-player", async (string id, HttpContext context, IAntiforgery antiforgery,
    MeepleDbContext db, ManualActivityLogger activity) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync();
    if (!int.TryParse(form["playerId"], out var playerId)) return Results.Redirect("/accounts?error=1");
    await using var transaction = await db.Database.BeginTransactionAsync();
    if (!await db.Users.AnyAsync(u => u.Id == id) || await db.Players.AnyAsync(p => p.AccountId == id))
        return Results.Redirect("/accounts?error=1");
    var player = await db.Players.SingleOrDefaultAsync(p => p.Id == playerId && p.AccountId == null);
    if (player is null) return Results.Redirect("/accounts?error=1");
    player.AccountId = id;
    if (!await db.AccountPlayers.AnyAsync(a => a.AccountId == id && a.PlayerId == playerId))
        db.AccountPlayers.Add(new() { AccountId = id, PlayerId = playerId });
    await db.SaveChangesAsync();
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO AccountGameSessions (AccountId, GameSessionId) SELECT {id}, GameSessionId FROM SessionPlayers WHERE PlayerId = {playerId}");
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO AccountPlayers (AccountId, PlayerId) SELECT {id}, other.PlayerId FROM SessionPlayers own JOIN SessionPlayers other ON other.GameSessionId = own.GameSessionId WHERE own.PlayerId = {playerId}");
    await transaction.CommitAsync();
    await activity.LogAsync(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value,
        "link_player", new { accountId = id, playerId });
    return Results.Redirect("/accounts?updated=1");
}).RequireAuthorization("Admin");
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization();
app.Run();

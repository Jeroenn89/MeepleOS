namespace MeepleOS.Web.Services;

internal static class AccountsPage
{
    public static IResult Render(string token, string playerOptions, string search, string role,
        int accountCount, int firstResult, int lastResult, string accountRows, string pagination,
        string activityLog, int activityCount, int failureCount, string latestActivity,
        bool showActivity, string message)
    {
        var userTab = showActivity ? "" : "active";
        var logTab = showActivity ? "active" : "";
        var countLabel = accountCount == 1 ? "account" : "accounts";
        var resultLabel = accountCount == 0 ? "Geen accounts gevonden." : $"Accounts {firstResult}–{lastResult} van {accountCount}";
        var content = showActivity ? $$"""
            <div class="admin-log-view">{{activityLog}}</div>
            """ : $$"""
            <div class="admin-grid">
              <div class="admin-primary">
                <details class="account-create">
                  <summary><span class="admin-tile-icon" aria-hidden="true"><svg><use href="#icon-person-add"/></svg></span><span class="account-create-copy"><strong>Nieuw account aanmaken</strong><small>Maak een nieuw gebruikersaccount aan voor MeepleOS.</small></span><span class="account-create-cta"><svg aria-hidden="true"><use href="#icon-plus"/></svg> Nieuw account aanmaken</span></summary>
                  <form method="post" action="/accounts"><input type="hidden" name="__RequestVerificationToken" value="{{token}}">
                    <label>Gebruikersnaam<input name="username" required></label>
                    <label>Tijdelijk wachtwoord<input name="password" type="password" autocomplete="new-password" required minlength="12"></label>
                    <label>Bestaande speler (optioneel)<select name="playerId"><option value="">Geen koppeling</option>{{playerOptions}}</select></label>
                    <button type="submit">Account aanmaken</button>
                  </form>
                </details>
                <section class="account-list" aria-labelledby="account-list-title">
                  <div class="account-list-heading"><div><h2 id="account-list-title">Bestaande accounts</h2><p>Zoek en filter in de bestaande gebruikersaccounts.</p></div><span class="account-count">{{accountCount}} {{countLabel}}</span></div>
                  <form class="account-search" method="get" action="/admin">
                    <label class="visually-hidden" for="account-query">Zoek op gebruikersnaam</label>
                    <span class="account-search-field"><svg aria-hidden="true"><use href="#icon-search"/></svg><input id="account-query" type="search" name="q" value="{{search}}" placeholder="Zoek op gebruikersnaam..."></span>
                    <label class="visually-hidden" for="account-role">Filter op rol</label>
                    <span class="account-role-field"><svg aria-hidden="true"><use href="#icon-filter"/></svg><select id="account-role" name="role" onchange="this.form.submit()"><option value="" {{(role == "" ? "selected" : "")}}>Alle rollen</option><option value="user" {{(role == "user" ? "selected" : "")}}>Gebruikers</option><option value="admin" {{(role == "admin" ? "selected" : "")}}>Beheerders</option></select></span>
                  </form>
                  <p class="account-result-count {{(accountCount > 0 ? "visually-hidden" : "")}}">{{resultLabel}}</p>
                  <div class="account-rows">{{accountRows}}</div>{{pagination}}
                </section>
              </div>
              <aside class="admin-activity-card" aria-labelledby="admin-activity-title">
                <div class="admin-activity-heading"><span class="admin-tile-icon" aria-hidden="true"><svg><use href="#icon-log"/></svg></span><div><h2 id="admin-activity-title">MCP-activiteiten</h2><p>Overzicht van de MCP-activiteiten.</p></div></div>
                <div class="admin-activity-stats">
                  <div><span class="stat-icon success" aria-hidden="true"><svg><use href="#icon-check"/></svg></span><span>Activiteiten vandaag</span><strong>{{activityCount}}</strong></div>
                  <div><span class="stat-icon failure" aria-hidden="true"><svg><use href="#icon-alert"/></svg></span><span>Fouten vandaag</span><strong>{{failureCount}}</strong></div>
                  <div><span class="stat-icon" aria-hidden="true"><svg><use href="#icon-clock"/></svg></span><span>Laatste activiteit</span><strong class="stat-date">{{latestActivity}}</strong></div>
                </div>
                <a class="admin-log-link" href="/admin?tab=activity"><svg aria-hidden="true"><use href="#icon-log"/></svg> Bekijk activiteitenlog <svg class="log-arrow" aria-hidden="true"><use href="#icon-arrow"/></svg></a>
              </aside>
            </div>
            """;

        return Results.Content($$"""
            <!doctype html>
            <html lang="nl">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>MeepleOS · Admin</title>
              <link rel="preconnect" href="https://fonts.googleapis.com">
              <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
              <link href="https://fonts.googleapis.com/css2?family=DM+Sans:wght@400;500;600;700&amp;family=Instrument+Serif:ital@0;1&amp;display=swap" rel="stylesheet">
              <link rel="stylesheet" href="/app.css">
              <link rel="icon" type="image/png" href="/meepleos-logo.png">
            </head>
            <body class="auth-body admin-body">
              <main class="admin-shell">
                <svg class="admin-icon-sprite" xmlns="http://www.w3.org/2000/svg" aria-hidden="true" focusable="false">
                  <symbol id="icon-users" viewBox="0 0 24 24"><circle cx="9" cy="8" r="3"/><path d="M2.5 19v-1.1a5.5 5.5 0 0 1 11 0V19H2.5Z"/><path d="M16.4 5.4a3 3 0 0 1 0 5.3M16 13a5.2 5.2 0 0 1 5.5 5.2V19H17"/></symbol>
                  <symbol id="icon-person-add" viewBox="0 0 24 24"><circle cx="9" cy="7" r="3.3"/><path d="M2.6 20v-1.6a6.4 6.4 0 0 1 11.2-4.2M18 13v8m-4-4h8"/></symbol>
                  <symbol id="icon-person" viewBox="0 0 24 24"><circle cx="12" cy="7" r="3.5"/><path d="M4.5 21v-1.8a7.5 7.5 0 0 1 15 0V21"/></symbol>
                  <symbol id="icon-log" viewBox="0 0 24 24"><rect x="4" y="2.5" width="16" height="19" rx="2"/><path d="M8 7h8M8 11h8M8 15h5"/></symbol>
                  <symbol id="icon-plus" viewBox="0 0 24 24"><path d="M12 3v18M3 12h18"/></symbol>
                  <symbol id="icon-search" viewBox="0 0 24 24"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/></symbol>
                  <symbol id="icon-filter" viewBox="0 0 24 24"><path d="M3 4h18l-7 8v6l-4 2v-8L3 4Z"/></symbol>
                  <symbol id="icon-check" viewBox="0 0 24 24"><path d="m4 12 5 5L20 6"/></symbol>
                  <symbol id="icon-alert" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 7v6m0 4h.01"/></symbol>
                  <symbol id="icon-clock" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l4 2"/></symbol>
                  <symbol id="icon-crown" viewBox="0 0 24 24"><path d="m2 7 4.5 4L12 4l5.5 7L22 7l-2 12H4L2 7Z"/></symbol>
                  <symbol id="icon-more" viewBox="0 0 24 24"><circle cx="4" cy="12" r="1.4" fill="currentColor" stroke="none"/><circle cx="12" cy="12" r="1.4" fill="currentColor" stroke="none"/><circle cx="20" cy="12" r="1.4" fill="currentColor" stroke="none"/></symbol>
                  <symbol id="icon-arrow" viewBox="0 0 24 24"><path d="M3 12h18m-7-7 7 7-7 7"/></symbol>
                </svg>
                <a class="admin-back" href="/">← &nbsp;Terug naar MeepleOS</a>
                <div class="admin-title"><span class="admin-title-icon" aria-hidden="true"><img src="/meeple-background-icon.png" width="56" height="56" alt=""></span><div><h1>Admin</h1><p>Beheer de accounts en activiteiten van MeepleOS.</p></div></div>
                <nav class="admin-tabs" aria-label="Admin-onderdelen">
                  <a class="{{userTab}}" href="/admin" {{(!showActivity ? "aria-current=\"page\"" : "")}}><svg aria-hidden="true"><use href="#icon-users"/></svg> Gebruikersbeheer</a>
                  <a class="{{logTab}}" href="/admin?tab=activity" {{(showActivity ? "aria-current=\"page\"" : "")}}><svg aria-hidden="true"><use href="#icon-log"/></svg> MCP-activiteitenlog</a>
                </nav>
                {{message}}
                {{content}}
              </main>
            </body>
            </html>
            """, "text/html; charset=utf-8");
    }
}

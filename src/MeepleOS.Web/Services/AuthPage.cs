using System.Net;

namespace MeepleOS.Web.Services;

internal static class AuthPage
{
    public static IResult Render(string title, string introduction, string form, string message = "", bool showBackLink = true, bool wide = false)
    {
        var backLink = showBackLink ? "<a class=\"auth-back\" href=\"/\">← Terug naar MeepleOS</a>" : "";
        return Results.Content($$"""
            <!doctype html>
            <html lang="nl">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <title>MeepleOS · {{WebUtility.HtmlEncode(title)}}</title>
                <link rel="preconnect" href="https://fonts.googleapis.com">
                <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
                <link href="https://fonts.googleapis.com/css2?family=DM+Sans:wght@400;500;600;700&amp;family=Instrument+Serif:ital@0;1&amp;display=swap" rel="stylesheet">
                <link rel="stylesheet" href="/app.css">
                <link rel="icon" type="image/png" href="/meepleos-logo.png">
            </head>
            <body class="auth-body">
                <header class="auth-header"><a class="brand" href="/"><img class="brand-logo" src="/meepleos-logo.png" alt="" width="36" height="44">MeepleOS</a></header>
                <main class="auth-main">
                    <section class="auth-card {{(wide ? "auth-card-wide" : "")}}" aria-labelledby="auth-title">
                        {{backLink}}
                        <h1 id="auth-title">{{WebUtility.HtmlEncode(title)}}</h1>
                        <p class="intro">{{WebUtility.HtmlEncode(introduction)}}</p>
                        {{message}}
                        {{form}}
                    </section>
                </main>
            </body>
            </html>
            """, "text/html; charset=utf-8");
    }
}

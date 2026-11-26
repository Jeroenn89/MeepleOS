using System.Net;
using System.Text;
using System.Text.Json;
using MeepleOS.DataLayer.Data.Context;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeepleOS.Web.Services;

internal static class ActivityLogSection
{
    public static async Task<string> RenderAsync(MeepleDbContext db, IQueryCollection queryString)
    {
        const int pageSize = 20;
        var accountId = queryString["logAccount"].ToString();
        var tool = queryString["logTool"].ToString();
        var status = queryString["logStatus"].ToString();
        var requestedPage = int.TryParse(queryString["logPage"], out var number) && number > 0 ? number : 1;
        var allLogs = db.ActivityLogs.AsNoTracking();
        var logs = allLogs;
        if (accountId.Length > 0) logs = logs.Where(x => x.AccountId == accountId);
        var filterStatus = status is "Success" or "Failed";
        var requests = logs.GroupBy(x => new
        {
            x.AccountId,
            x.RequestId,
            StandaloneId = x.RequestId == null ? x.Id : 0
        });
        if (tool.Length > 0 || filterStatus)
            requests = requests.Where(group => group.Any(x =>
                (tool.Length == 0 || x.ToolName == tool) && (!filterStatus || x.Status == status)));

        var count = await requests.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        var page = Math.Min(requestedPage, pages);
        var groups = await requests.Select(group => new
            {
                group.Key.AccountId,
                group.Key.RequestId,
                group.Key.StandaloneId,
                StartedAt = group.Min(x => x.CreatedAt),
                FirstId = group.Min(x => x.Id)
            })
            .OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.FirstId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var requestIds = groups.Where(x => x.RequestId.HasValue).Select(x => x.RequestId!.Value).ToArray();
        var standaloneIds = groups.Where(x => !x.RequestId.HasValue).Select(x => x.StandaloneId).ToArray();
        var rows = await logs.Where(x => x.RequestId.HasValue
                ? requestIds.Contains(x.RequestId.Value)
                : standaloneIds.Contains(x.Id))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();
        var callsByRequest = rows.ToLookup(x => (x.AccountId, x.RequestId, StandaloneId: x.RequestId == null ? x.Id : 0));
        var accounts = await allLogs.GroupBy(x => new { x.AccountId, x.AccountName })
            .Select(x => new { x.Key.AccountId, x.Key.AccountName }).OrderBy(x => x.AccountName).ToListAsync();
        var tools = await allLogs.Select(x => x.ToolName).Distinct().OrderBy(x => x).ToListAsync();
        static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
        static string Option(string value, string label, string selected) =>
            $"<option value=\"{H(value)}\"{(value == selected ? " selected" : "")}>{H(label)}</option>";
        string Url(int target) => "/admin?logPage=" + target
            + "&logAccount=" + Uri.EscapeDataString(accountId)
            + "&logTool=" + Uri.EscapeDataString(tool)
            + "&logStatus=" + Uri.EscapeDataString(status);

        var html = new StringBuilder();
        html.Append("<section class=\"activity-log\" aria-labelledby=\"activity-title\"><h2 id=\"activity-title\">MCP-activiteitenlog</h2>");
        html.Append("<form method=\"get\" action=\"/admin\" class=\"activity-filters\">");
        html.Append("<label>Account<select name=\"logAccount\">").Append(Option("", "Alle accounts", accountId));
        foreach (var account in accounts)
            html.Append(Option(account.AccountId, $"{account.AccountName} ({account.AccountId})", accountId));
        html.Append("</select></label><label>Tool of actie<select name=\"logTool\">").Append(Option("", "Alle tools en acties", tool));
        foreach (var name in tools) html.Append(Option(name, name, tool));
        html.Append("</select></label><label>Status<select name=\"logStatus\">");
        html.Append(Option("", "Alle statussen", status)).Append(Option("Success", "Success", status))
            .Append(Option("Failed", "Failed", status));
        html.Append("</select></label><button type=\"submit\">Filteren</button></form>");
        html.Append($"<p class=\"account-result-count\">{count} {(count == 1 ? "item" : "items")} · Pagina {page} van {pages}</p>");
        foreach (var group in groups)
        {
            var calls = callsByRequest[(group.AccountId, group.RequestId, group.StandaloneId)].ToList();
            if (!group.RequestId.HasValue)
            {
                html.Append("<article>");
                foreach (var call in calls) AppendCall(call);
                html.Append("</article>");
                continue;
            }

            var firstCall = calls[0];
            var failures = calls.Count(x => x.Status == "Failed");
            var callCount = calls.Count == 1 ? "1 toolaanroep" : $"{calls.Count} toolaanroepen";
            html.Append($$"""
                <article><details><summary><time>{{Date(firstCall)}}</time><strong>{{H(firstCall.AccountName)}}</strong>
                <span>Chatverzoek · {{callCount}}</span><span class="activity-status {{(failures > 0 ? "activity-failed" : "")}}">{{(failures > 0 ? $"{failures} mislukt" : "Success")}}</span></summary>
                <ol class="activity-request-calls">
                """);
            foreach (var call in calls)
            {
                html.Append("<li>");
                AppendCall(call);
                html.Append("</li>");
            }
            html.Append("</ol></details></article>");
        }
        if (pages > 1)
            html.Append($"<nav class=\"account-pagination\" aria-label=\"Activiteitenpagina's\">{(page > 1 ? $"<a href=\"{H(Url(page - 1))}\">Vorige</a>" : "<span></span>")}<span>Pagina {page} van {pages}</span>{(page < pages ? $"<a href=\"{H(Url(page + 1))}\">Volgende</a>" : "<span></span>")}</nav>");
        html.Append("</section>");
        return html.ToString();

        static string Date(ActivityLog log) => H(log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));

        void AppendCall(ActivityLog log)
        {
            string formatted;
            try
            {
                using var document = JsonDocument.Parse(log.Parameters);
                formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { formatted = log.Parameters; }
            var date = Date(log);
            html.Append($$"""
                <details><summary><time>{{date}}</time><strong>{{H(log.AccountName)}}</strong><span>{{H(log.ToolName)}}</span><span class="activity-status {{(log.Status == "Failed" ? "activity-failed" : "")}}">{{H(log.Status)}}</span></summary>
                <dl><dt>Accountnaam</dt><dd>{{H(log.AccountName)}}</dd><dt>AccountId</dt><dd>{{H(log.AccountId)}}</dd>
                <dt>ToolName</dt><dd>{{H(log.ToolName)}}</dd><dt>Parameters</dt><dd><pre>{{H(formatted)}}</pre></dd>
                <dt>Status</dt><dd>{{H(log.Status)}}</dd><dt>Foutmelding</dt><dd>{{H(log.ErrorMessage ?? "—")}}</dd>
                <dt>Datum en tijd</dt><dd>{{date}}</dd></dl></details>
                """);
        }
    }
}

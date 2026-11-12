using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

public sealed record GameSearchHit(string Title, string Url, string Content);

public interface IGameSearch
{
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default);
}

public sealed class TavilyGameSearch(HttpClient http, string apiKey) : IGameSearch
{
    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return [];
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
        {
            Content = JsonContent.Create(new
            {
                query = $"{game} board game player count playing time",
                search_depth = "basic",
                max_results = 5,
                include_answer = false,
                include_raw_content = false,
                auto_parameters = false
            })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired)
            return []; // No retry or paid search fallback.
        if (!response.IsSuccessStatusCode) return [];
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return [];
        return results.EnumerateArray().Take(5).Select(hit => new GameSearchHit(
            hit.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            hit.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
            hit.TryGetProperty("content", out var content) ? content.GetString() ?? "" : ""))
            .Where(hit => Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                && (hit.Title.Contains(game, StringComparison.OrdinalIgnoreCase) || hit.Content.Contains(game, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}

internal static class GameSearchEvidence
{
    public static bool Supports(IReadOnlyList<GameSearchHit> hits, int value, bool duration)
    {
        if (value < 1) return false;
        var number = $@"(?<!\d){value}(?!\d)";
        var topic = duration ? @"(?:min(?:ute)?s?|minuten|speelduur|playing\s*time|play\s*time|duration)"
            : @"(?:players?|spelers?|player\s*count|aantal\s*spelers)";
        return hits.Any(hit => Regex.IsMatch(hit.Title + " " + hit.Content,
            $@"(?:{number}.{{0,45}}{topic}|{topic}.{{0,45}}{number})", RegexOptions.IgnoreCase | RegexOptions.Singleline));
    }

    public static string Context(IReadOnlyList<GameSearchHit> hits) => string.Join("\n", hits.Select(hit =>
        $"Bron: {hit.Title} ({hit.Url}) — {hit.Content[..Math.Min(hit.Content.Length, 900)]}"));
}

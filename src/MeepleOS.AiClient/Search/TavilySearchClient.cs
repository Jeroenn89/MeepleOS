using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeepleOS.AiClient;

internal sealed class TavilySearchClient(HttpClient http, string apiKey, Action<string>? onTrace)
{
    internal int RequestCount { get; private set; }

    internal async Task<IReadOnlyList<GameSearchHit>> QueryAsync(string query, CancellationToken ct)
    {
        onTrace?.Invoke($"Tavily-zoekopdracht: {query}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
        {
            Content = JsonContent.Create(new
            {
                query,
                search_depth = "basic",
                max_results = 5,
                include_answer = false,
                include_raw_content = false,
                auto_parameters = false
            })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        HttpResponseMessage response;
        try
        {
            RequestCount++;
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException) { throw new GameSearchUnavailableException("Tavily is tijdelijk niet bereikbaar. Probeer het later opnieuw."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new GameSearchUnavailableException("Tavily reageerde niet op tijd. Probeer het later opnieuw."); }
        using (response)
        {
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired)
            throw new GameSearchUnavailableException("Tavily heeft zijn zoeklimiet bereikt. Probeer het later opnieuw.");
        if (!response.IsSuccessStatusCode) throw new GameSearchUnavailableException($"Tavily is tijdelijk niet beschikbaar (HTTP {(int)response.StatusCode}).");
        JsonDocument json;
        try { json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); }
        catch (JsonException) { throw new GameSearchUnavailableException("Tavily gaf geen leesbaar zoekresultaat. Probeer het later opnieuw."); }
        using (json)
        {
        if (!json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            onTrace?.Invoke("Tavily gaf geen resultatenlijst terug.");
            return [];
        }
        var returned = results.EnumerateArray().Take(5).Select(hit => new GameSearchHit(
            hit.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            hit.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
            hit.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "")).ToList();
        if (results.GetArrayLength() > 5) onTrace?.Invoke($"Tavily: {results.GetArrayLength() - 5} extra resultaten buiten de ingestelde limiet van 5.");
        return returned;
        }
        }
    }


}

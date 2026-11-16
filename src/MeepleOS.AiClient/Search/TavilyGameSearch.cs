using static MeepleOS.AiClient.GamePageSelector;

namespace MeepleOS.AiClient;

public sealed partial class TavilyGameSearch(HttpClient http, string apiKey, Action<string>? onTrace = null) : IGameSearch
{
    private readonly TavilySearchClient searchClient = new(http, apiKey, onTrace);
    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);
    public int RequestCount => searchClient.RequestCount;
    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default)
        => await SearchAsync(game, ["minimumaantal spelers", "maximumaantal spelers", "speelduur"], ct);

    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts, CancellationToken ct = default)
        => await SearchAsync(game, missingFacts, null, [], ct);

    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts,
        int? preferredBggId, IReadOnlyCollection<int> excludedBggIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            onTrace?.Invoke("Tavily overgeslagen: API-sleutel ontbreekt.");
            return [];
        }

        var terms = new List<string>();
        if (missingFacts.Any(fact => fact.Contains("spelers", StringComparison.OrdinalIgnoreCase)))
            terms.Add("player count");
        if (missingFacts.Any(fact => fact.Contains("speelduur", StringComparison.OrdinalIgnoreCase)))
            terms.Add("playing time minutes");
        if (terms.Count == 0)
        {
            onTrace?.Invoke("Tavily overgeslagen: geen zoektermen voor de ontbrekende spelgegevens.");
            return [];
        }

        var searchTerms = string.Join(" ", terms);
        if (preferredBggId is { } selectedId)
        {
            var selected = await searchClient.QueryAsync($"site:boardgamegeek.com/boardgame/{selectedId} {searchTerms}", ct);
            return MergeHits(selected.Where(hit => BggId(hit) == selectedId));
        }

        var retryChoices = excludedBggIds.Count > 0;
        var bggQuery = retryChoices
            ? $"site:boardgamegeek.com/boardgame/ \"{game}\" board game other edition {searchTerms}"
            : $"site:boardgamegeek.com/boardgame/ {game} {searchTerms}";
        var bggResults = await searchClient.QueryAsync(bggQuery, ct);
        bggResults = bggResults.Where(hit => BggId(hit) is not { } id || !excludedBggIds.Contains(id)).ToArray();
        var queryCount = 1;
        foreach (var hit in bggResults)
            onTrace?.Invoke($"BGG-kandidaat {hit.Url}: {(IsMatchingBgg(hit, game) ? "passende spelpagina" : "geen passende spelpagina")}.");
        var bgg = SelectBgg(bggResults, game);
        if (bgg.Count == 0)
            onTrace?.Invoke(bggResults.Count == 0
                ? "BGG-zoekopdracht gaf geen resultaten."
                : $"BGG-zoekopdracht gaf {bggResults.Count} resultaten, maar geen passend basisspel.");
        if (bgg.Count > 0 && GameTitleMatcher.Tokens(game, ignoreWords: true).Length == 1
            && BggScore(bgg[0], game) > 0)
        {
            var identity = await searchClient.QueryAsync($"site:boardgamegeek.com/boardgame/ \"{game}\"", ct);
            queryCount++;
            bgg = SelectBgg(bgg.Concat(identity.Where(hit => IsBgg(hit)
                && (BggId(hit) is not { } id || !excludedBggIds.Contains(id)))), game);
        }
        var searchedBggId = false;
        if (bgg.Count > 0 && bgg.All(hit => !IsCanonicalBgg(hit)))
        {
            var id = BggId(bgg[0])!.Value;
            var primary = await searchClient.QueryAsync($"site:boardgamegeek.com/boardgame/{id} {GameTitleMatcher.PageTitle(bgg[0].Title)} players playing time", ct);
            queryCount++;
            bgg = SelectBgg(bgg.Concat(primary.Where(IsBgg)), game);
            searchedBggId = true;
        }
        if (missingFacts.All(fact => GameSearchEvidence.HasFact(bgg, fact)))
        {
            onTrace?.Invoke($"BGG leverde alle spelgegevens voor {game}; verdere Tavily-zoekactie overgeslagen.");
            return bgg;
        }

        var general = await searchClient.QueryAsync($"\"{game}\" board game publisher {searchTerms}", ct);
        queryCount++;
        bgg = SelectBgg(bgg.Concat(general.Where(hit => IsBgg(hit)
            && (BggId(hit) is not { } id || !excludedBggIds.Contains(id)))), game);
        var hits = MergeHits(bgg.Concat(general.Where(hit => !IsBgg(hit) && IsMatchingGeneral(hit, game))));
        if (hits.Count == 0)
            onTrace?.Invoke(general.Count == 0
                ? "Ook de algemene zoekopdracht gaf geen resultaten."
                : $"De algemene zoekopdracht gaf {general.Count} resultaten, maar geen passende spelpagina.");
        if (missingFacts.All(fact => GameSearchEvidence.HasFact(hits, fact)))
            return hits;

        var unresolved = missingFacts.Where(fact => !GameSearchEvidence.HasFact(hits, fact)).ToArray();
        if (unresolved.Length == 0 || queryCount >= 4)
            return hits;

        var unresolvedTerms = string.Join(" ", unresolved.Select(fact => fact == "speelduur" ? "playing time minutes" : "players"));
        var retryQuery = bgg.Count > 0 && !searchedBggId
            ? $"site:boardgamegeek.com/boardgame/{BggId(bgg[0])} {unresolvedTerms}"
            : $"\"{game}\" board game rules {unresolvedTerms}";
        var retry = await searchClient.QueryAsync(retryQuery, ct);
        bgg = SelectBgg(bgg.Concat(retry.Where(hit => IsBgg(hit)
            && (BggId(hit) is not { } id || !excludedBggIds.Contains(id)))), game);
        return MergeHits(bgg.Concat(hits.Where(hit => !IsBgg(hit)))
            .Concat(retry.Where(hit => !IsBgg(hit) && IsMatchingGeneral(hit, game))));
    }

    public Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, string? alternativeGameName,
        IReadOnlyCollection<string> missingFacts, int? preferredBggId,
        IReadOnlyCollection<int> excludedBggIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(alternativeGameName)
            || game.Equals(alternativeGameName, StringComparison.OrdinalIgnoreCase)
            || preferredBggId is not null)
            return SearchAsync(game, missingFacts, preferredBggId, excludedBggIds, ct);

        return SearchTranslatedAsync(game, alternativeGameName, missingFacts, excludedBggIds, ct);
    }

    internal static int? BggId(GameSearchHit hit) => GamePageSelector.BggId(hit);
    internal static bool NeedsNameReview(GameSearchHit hit, string game) => GamePageSelector.NeedsNameReview(hit, game);
    internal static string BggTitle(GameSearchHit hit, string game) => GamePageSelector.BggTitle(hit, game);
    internal static bool IsMatchingBgg(GameSearchHit hit, string game) => GamePageSelector.IsMatchingBgg(hit, game);
}

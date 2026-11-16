using System.Text.RegularExpressions;
using static MeepleOS.AiClient.GamePageSelector;

namespace MeepleOS.AiClient;

public sealed partial class TavilyGameSearch
{
    private async Task<IReadOnlyList<GameSearchHit>> SearchTranslatedAsync(string requestedName,
        string alternativeName, IReadOnlyCollection<string> missingFacts,
        IReadOnlyCollection<int> excludedBggIds, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            onTrace?.Invoke("Tavily overgeslagen: API-sleutel ontbreekt.");
            return [];
        }

        var needsPlayers = missingFacts.Any(fact => fact.Contains("spelers", StringComparison.OrdinalIgnoreCase));
        var needsDuration = missingFacts.Any(fact => fact.Contains("speelduur", StringComparison.OrdinalIgnoreCase));
        var dutchTerms = string.Join(" ", new[]
        {
            needsPlayers ? "spelers" : "",
            needsDuration ? "speelduur" : ""
        }.Where(term => term.Length > 0));
        var englishTerms = string.Join(" ", new[]
        {
            needsPlayers ? "players" : "",
            needsDuration ? "playing time minutes" : ""
        }.Where(term => term.Length > 0));
        if (dutchTerms.Length == 0)
            return [];

        var localResults = await searchClient.QueryAsync($"\"{requestedName}\" bordspel {dutchTerms}", ct);
        var localMatches = MatchingHits(localResults, requestedName, excludedBggIds);
        if (missingFacts.All(fact => GameSearchEvidence.HasFact(localMatches, fact)))
            return localMatches;

        var internationalResults = await searchClient.QueryAsync(
            $"\"{alternativeName}\" board game {englishTerms}", ct);
        var internationalMatches = MatchingHits(internationalResults, alternativeName, excludedBggIds);
        if (internationalMatches.Count == 0)
            return localMatches;

        var aliasEvidence = localResults.Concat(internationalResults).ToArray();
        if (!aliasEvidence.Any(hit => ProvesSameGame(hit, requestedName, alternativeName)))
        {
            // De alternatieve titel van Gemini is een zoekhint, geen bewijs voor dezelfde editie.
            var bridge = await searchClient.QueryAsync($"\"{requestedName}\" \"{alternativeName}\" bordspel", ct);
            if (!bridge.Any(hit => ProvesSameGame(hit, requestedName, alternativeName)))
            {
                onTrace?.Invoke($"Geen bron bevestigt dat {requestedName} en {alternativeName} hetzelfde basisspel zijn.");
                return localMatches;
            }
        }

        onTrace?.Invoke($"Alternatieve titel {alternativeName} bevestigd voor {requestedName}.");
        return MergeHits(localMatches.Concat(internationalMatches));
    }

    private static List<GameSearchHit> MatchingHits(IEnumerable<GameSearchHit> results, string name,
        IReadOnlyCollection<int> excludedBggIds)
    {
        var allowed = results.Where(hit => BggId(hit) is not { } id || !excludedBggIds.Contains(id)).ToArray();
        var bgg = SelectBgg(allowed.Where(IsBgg), name);
        return MergeHits(bgg.Concat(allowed.Where(hit => !IsBgg(hit) && IsMatchingGeneral(hit, name))));
    }

    private static bool ProvesSameGame(GameSearchHit hit, string requestedName, string alternativeName)
    {
        var matchesBaseGame = IsMatchingBgg(hit, requestedName) || IsMatchingBgg(hit, alternativeName)
            || IsMatchingGeneral(hit, requestedName) || IsMatchingGeneral(hit, alternativeName);
        if (!matchesBaseGame)
            return false;

        var page = $"{hit.Title} {hit.Content}";
        return Mentions(page, requestedName) && Mentions(page, alternativeName);
    }

    private static bool Mentions(string text, string name)
        => Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

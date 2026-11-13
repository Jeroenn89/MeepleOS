using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

public sealed record GameSearchHit(string Title, string Url, string Content);
public sealed class GameSearchUnavailableException(string message) : Exception(message);

public interface IGameSearch
{
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default);
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts, CancellationToken ct = default)
        => SearchAsync(game, ct);
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts,
        int? preferredBggId, IReadOnlyCollection<int> excludedBggIds, CancellationToken ct = default)
        => SearchAsync(game, missingFacts, ct);
}

public sealed class TavilyGameSearch(HttpClient http, string apiKey, Action<string>? onTrace = null) : IGameSearch
{
    private static readonly Regex Alias = new(@"\b(?:also\s+known\s+as|alternat(?:e|ive)\s+(?:names?|titles?)|alternatieve\s+(?:namen?|titels?)|ook\s+bekend\s+als)\s*:?\s*(?<name>[^.;+\r\n|]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);
    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default)
        => await SearchAsync(game, ["minimumaantal spelers", "maximumaantal spelers", "speelduur"], ct);

    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts, CancellationToken ct = default)
        => await SearchAsync(game, missingFacts, null, [], ct);

    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts,
        int? preferredBggId, IReadOnlyCollection<int> excludedBggIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { onTrace?.Invoke("Tavily overgeslagen: API-sleutel ontbreekt."); return []; }
        var terms = new List<string>();
        if (missingFacts.Any(fact => fact.Contains("spelers", StringComparison.OrdinalIgnoreCase))) terms.Add("player count");
        if (missingFacts.Any(fact => fact.Contains("speelduur", StringComparison.OrdinalIgnoreCase))) terms.Add("playing time minutes");
        if (terms.Count == 0) { onTrace?.Invoke("Tavily overgeslagen: geen zoektermen voor de ontbrekende spelgegevens."); return []; }
        if (preferredBggId is { } selectedId)
        {
            var selected = await QueryAsync($"site:boardgamegeek.com/boardgame/{selectedId} {string.Join(" ", terms)}", ct);
            return MergeHits(selected.Where(hit => BggId(hit) == selectedId));
        }
        var retryChoices = excludedBggIds.Count > 0;
        var bggResults = await QueryAsync(retryChoices
            ? $"site:boardgamegeek.com/boardgame/ \"{game}\" board game other edition {string.Join(" ", terms)}"
            : $"site:boardgamegeek.com/boardgame/ {game} {string.Join(" ", terms)}", ct);
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
            var identity = await QueryAsync($"site:boardgamegeek.com/boardgame/ \"{game}\"", ct);
            queryCount++;
            bgg = SelectBgg(bgg.Concat(identity.Where(hit => IsBgg(hit)
                && (BggId(hit) is not { } id || !excludedBggIds.Contains(id)))), game);
        }
        var searchedBggId = false;
        if (bgg.Count > 0 && bgg.All(hit => !IsCanonicalBgg(hit)))
        {
            var id = BggId(bgg[0])!.Value;
            var primary = await QueryAsync($"site:boardgamegeek.com/boardgame/{id} {GameTitleMatcher.PageTitle(bgg[0].Title)} players playing time", ct);
            queryCount++;
            bgg = SelectBgg(bgg.Concat(primary.Where(IsBgg)), game);
            searchedBggId = true;
        }
        if (missingFacts.All(fact => GameSearchEvidence.HasFact(bgg, fact)))
        {
            onTrace?.Invoke($"BGG leverde alle spelgegevens voor {game}; verdere Tavily-zoekactie overgeslagen.");
            return bgg;
        }

        var general = await QueryAsync($"\"{game}\" board game publisher {string.Join(" ", terms)}", ct);
        queryCount++;
        bgg = SelectBgg(bgg.Concat(general.Where(hit => IsBgg(hit)
            && (BggId(hit) is not { } id || !excludedBggIds.Contains(id)))), game);
        var hits = MergeHits(bgg.Concat(general.Where(hit => !IsBgg(hit) && IsMatchingGeneral(hit, game))));
        if (hits.Count == 0)
            onTrace?.Invoke(general.Count == 0
                ? "Ook de algemene zoekopdracht gaf geen resultaten."
                : $"De algemene zoekopdracht gaf {general.Count} resultaten, maar geen passende spelpagina.");
        if (missingFacts.All(fact => GameSearchEvidence.HasFact(hits, fact))) return hits;

        var unresolved = missingFacts.Where(fact => !GameSearchEvidence.HasFact(hits, fact)).ToArray();
        if (unresolved.Length == 0 || queryCount >= 4) return hits;
        var retry = await QueryAsync(bgg.Count > 0 && !searchedBggId
            ? $"site:boardgamegeek.com/boardgame/{BggId(bgg[0])} {string.Join(" ", unresolved.Select(fact => fact == "speelduur" ? "playing time minutes" : "players"))}"
            : $"\"{game}\" board game rules {string.Join(" ", unresolved.Select(fact => fact == "speelduur" ? "playing time minutes" : "players"))}", ct);
        bgg = SelectBgg(bgg.Concat(retry.Where(hit => IsBgg(hit)
            && (BggId(hit) is not { } id || !excludedBggIds.Contains(id)))), game);
        return MergeHits(bgg.Concat(hits.Where(hit => !IsBgg(hit)))
            .Concat(retry.Where(hit => !IsBgg(hit) && IsMatchingGeneral(hit, game))));
    }

    private static List<GameSearchHit> SelectBgg(IEnumerable<GameSearchHit> results, string game)
    {
        var candidates = results.Where(hit => IsMatchingBgg(hit, game))
            .OrderBy(hit => BggScore(hit, game)).ThenByDescending(IsCanonicalBgg).ToList();
        if (candidates.FirstOrDefault() is not { } first) return [];
        var firstId = BggId(first);
        if (candidates.Any(hit => BggId(hit) != firstId
            && GameTitleMatcher.PageTitle(hit.Title).Equals(GameTitleMatcher.PageTitle(first.Title),
                StringComparison.OrdinalIgnoreCase)))
        {
            var choices = candidates.Where(hit => BggId(hit) != null
                && GameTitleMatcher.PageTitle(hit.Title).Equals(GameTitleMatcher.PageTitle(first.Title),
                    StringComparison.OrdinalIgnoreCase)).GroupBy(hit => BggId(hit))
                .Select(group => group.First()).ToArray();
            throw new ClarificationNeededException($"Welke editie van {GameTitleMatcher.PageTitle(first.Title)} bedoel je: "
                + $"{string.Join(" of ", choices.Select((hit, index) => $"de {index + 1}e (BGG {BggId(hit)})"))}?", choices);
        }
        var firstScore = BggScore(first, game);
        var alternatives = candidates.Where(hit => BggId(hit) != firstId
            && BggScore(hit, game) == firstScore)
            .Select(hit => GameTitleMatcher.PageTitle(hit.Title))
            .Where(title => !title.Equals(GameTitleMatcher.PageTitle(first.Title), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (alternatives.Length > 0)
            throw new ClarificationNeededException($"Welk bordspel bedoel je: {GameTitleMatcher.PageTitle(first.Title)} of {string.Join(" of ", alternatives)}?",
                candidates.Where(hit => BggId(hit) != null && BggScore(hit, game) == firstScore)
                    .GroupBy(hit => BggId(hit)).Select(group => group.First()).ToArray());
        return MergeHits(candidates.Where(hit => BggId(hit) == firstId));
    }

    internal static int? BggId(GameSearchHit hit)
    {
        if (!IsBgg(hit) || !Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)) return null;
        var match = Regex.Match(uri.AbsolutePath, @"^/boardgame/(?<id>\d+)(?:/|$)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["id"].Value, out var id) ? id : null;
    }

    internal static bool NeedsNameReview(GameSearchHit hit, string game) =>
        GameTitleMatcher.Tokens(game, ignoreWords: true).Length == 1 && BggScore(hit, game) > 0;

    internal static string BggTitle(GameSearchHit hit, string game)
    {
        var title = GameTitleMatcher.PageTitle(hit.Title);
        if (GameTitleMatcher.SearchScore(game, title).HasValue
            || Alias.Matches(hit.Content).Any(match => GameTitleMatcher.SearchScore(game, match.Groups["name"].Value).HasValue))
            return title;
        var slug = new Uri(hit.Url).AbsolutePath.Split('/').Skip(3).FirstOrDefault() ?? game;
        return Uri.UnescapeDataString(slug).Replace('-', ' ').Trim();
    }

    private static bool IsCanonicalBgg(GameSearchHit hit) =>
        BggId(hit).HasValue && Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
        && Regex.IsMatch(uri.AbsolutePath, @"^/boardgame/\d+(?:/[^/]+)?/?$", RegexOptions.IgnoreCase);

    private static int BggScore(GameSearchHit hit, string game)
    {
        var scores = new List<int>();
        if (GameTitleMatcher.SearchScore(game, GameTitleMatcher.PageTitle(hit.Title)) is { } titleScore)
            scores.Add(titleScore);
        if (Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
            && GameTitleMatcher.SearchScore(game,
                Uri.UnescapeDataString(uri.AbsolutePath.Split('/').Skip(3).FirstOrDefault() ?? "")) is { } slugScore)
            scores.Add(slugScore);
        scores.AddRange(Alias.Matches(hit.Content)
            .Select(match => GameTitleMatcher.SearchScore(game, match.Groups["name"].Value))
            .Where(score => score.HasValue).Select(score => score!.Value));
        return scores.Count == 0 ? 100 : scores.Min();
    }

    private static List<GameSearchHit> MergeHits(IEnumerable<GameSearchHit> hits) => hits
        .GroupBy(hit => hit.Url, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First() with
        {
            Content = string.Join(" ", group.Select(hit => hit.Content).Distinct(StringComparer.Ordinal))
        }).ToList();

    private async Task<IReadOnlyList<GameSearchHit>> QueryAsync(string query, CancellationToken ct)
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
        try { response = await http.SendAsync(request, ct); }
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

    private static bool IsBgg(GameSearchHit hit) => Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("boardgamegeek.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.boardgamegeek.com", StringComparison.OrdinalIgnoreCase));

    internal static bool IsMatchingBgg(GameSearchHit hit, string game)
    {
        if (!IsBgg(hit) || !Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
            || BggId(hit) is null) return false;
        if (Regex.IsMatch(hit.Title, @"\b(?:expansion|uitbreiding)\b", RegexOptions.IgnoreCase)) return false;
        var title = GameTitleMatcher.PageTitle(hit.Title);
        var slug = Uri.UnescapeDataString(uri.AbsolutePath.Split('/').Skip(3).FirstOrDefault() ?? "");
        return GameTitleMatcher.SearchScore(game, title).HasValue
            || GameTitleMatcher.SearchScore(game, slug).HasValue
            || Alias.Matches(hit.Content).Any(match => GameTitleMatcher.SearchScore(game, match.Groups["name"].Value).HasValue);
    }

    private static bool IsMatchingGeneral(GameSearchHit hit, string game)
    {
        if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.AbsolutePath.Trim('/').Length == 0) return false;
        if (Regex.IsMatch(hit.Title + " " + uri.AbsolutePath, @"\b(?:expansion|uitbreiding)\b", RegexOptions.IgnoreCase)) return false;
        return GameTitleMatcher.SearchScore(game, GameTitleMatcher.PageTitle(hit.Title)).HasValue
            || GameTitleMatcher.SearchScore(game, Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').Last())).HasValue
            || Alias.Matches(hit.Content).Any(match => GameTitleMatcher.SearchScore(game, match.Groups["name"].Value).HasValue);
    }

}

internal static class GameSearchEvidence
{
    private static readonly Regex PlayerRange = new(@"(?<!\d)(?<min>\d+)\s*(?:-|–|—|to|tot)\s*(?<max>\d+)\s*(?:players?|spelers?)\b|\b(?:players?|spelers?)\s*:?\s*(?<min2>\d+)\s*(?:-|–|—|to|tot)\s*(?<max2>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Minimum = new(@"\b(?:minimum|min(?:imum)?\.?)\s*(?:of\s*)?(?<value>\d+)\s*(?:players?|spelers?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Maximum = new(@"\b(?:maximum|max(?:imum)?\.?)\s*(?:of\s*)?(?<value>\d+)\s*(?:players?|spelers?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DurationLabel = new(@"\b(?:playing\s*time|play\s*time|playtime|duration|speelduur|speeltijd|game\s*time)\s*:?\s*(?<min>\d+)\s*(?:(?:-|–|—|to|tot)\s*(?<max>\d+))?\s*(?:minutes?|minuten|min\.?)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DurationValue = new(@"(?<![\d-])(?<min>\d+)\s*(?:(?:-|–|—|to|tot)\s*(?<max>\d+))?\s*(?:minutes?|minuten|min\.?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Instructional = new(@"\b(?:learn|how\s+to\s+play|tutorial|rules|uitleg|spelregels|video|review)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlayerCount = new(@"\b(?:players?|spelers?|player\s*count|aantal\s+spelers)\s*:?\s*(?<min>\d+)\s*(?:-|–|—|to|tot|t/m)\s*(?<max>\d+)\b|\b(?:for|voor|with|met)\s+(?<min2>\d+)\s*(?:-|–|—|to|tot|t/m)\s*(?<max2>\d+)\s*(?:players?|spelers?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal sealed record Facts(int? MinPlayers, int? MaxPlayers, int? EstimatedMinutes)
    {
        public bool Complete => MinPlayers is > 0 && MaxPlayers >= MinPlayers && EstimatedMinutes is > 0;
    }

    internal sealed record Decision(int? Value, IReadOnlyList<int> Candidates,
        IReadOnlyList<GameSearchHit> Sources, string Reason);

    public static Facts Extract(IReadOnlyList<GameSearchHit> hits)
    {
        return new Facts(Resolve(hits, "minimumaantal spelers").Value,
            Resolve(hits, "maximumaantal spelers").Value, Resolve(hits, "speelduur").Value);
    }

    public static IReadOnlyList<int> Values(IReadOnlyList<GameSearchHit> hits, string fact)
    {
        var decision = Resolve(hits, fact);
        return decision.Value is { } value ? [value] : decision.Candidates;
    }

    public static Decision Resolve(IReadOnlyList<GameSearchHit> hits, string fact)
    {
        foreach (var rank in new[] { 4, 3, 2, 1, 0 })
        {
            var evidence = hits.Where(hit => Rank(hit) == rank)
                .Select(hit => (Hit: hit, Values: RawValues([hit], fact), Strength: Strength(hit, fact)))
                .Where(item => item.Values.Count == 1)
                .Select(item => (item.Hit, Value: item.Values[0], item.Strength)).ToList();
            if (evidence.Count == 0) continue;
            var grouped = evidence.GroupBy(item => item.Value).ToList();
            if (grouped.Count == 1)
                return Chosen(grouped[0].Key, evidence, $"{TierName(rank)} vermeldt een eenduidige waarde");

            var supported = grouped.Select(group => new
            {
                Value = group.Key,
                Domains = group.Select(item => new Uri(item.Hit.Url).Host).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            }).OrderByDescending(item => item.Domains).ToArray();
            if (supported[0].Domains >= 2 && supported[0].Domains > supported[1].Domains)
                return Chosen(supported[0].Value, evidence, $"{TierName(rank)}: meerdere onafhankelijke bronnen stemmen overeen");

            var strongest = evidence.Max(item => item.Strength);
            var strongestValues = evidence.Where(item => item.Strength == strongest).Select(item => item.Value).Distinct().ToArray();
            if (strongestValues.Length == 1
                && strongest >= evidence.Where(item => item.Value != strongestValues[0]).Max(item => item.Strength) + 2)
                return Chosen(strongestValues[0], evidence, $"{TierName(rank)}: expliciete spelgegevens wegen zwaarder dan een los getal");

            return new Decision(null, grouped.Select(group => group.Key).Order().ToArray(), [],
                $"tegenstrijdige gegevens van {TierName(rank)}");
        }
        return new Decision(null, [], [], "geen betrouwbare spelgegevens in passende bronnen");

        static Decision Chosen(int value, List<(GameSearchHit Hit, int Value, int Strength)> evidence, string reason) =>
            new(value, [value], evidence.Where(item => item.Value == value)
                .OrderByDescending(item => item.Strength).Select(item => item.Hit).ToArray(), reason);
    }

    private static IReadOnlyList<int> RawValues(IReadOnlyList<GameSearchHit> hits, string fact)
    {
        var values = new HashSet<int>();
        foreach (var hit in hits)
        {
            var content = hit.Content;
            if (fact is "minimumaantal spelers" or "maximumaantal spelers")
            {
                foreach (Match match in PlayerRange.Matches(content))
                    Add(match.Groups[fact == "minimumaantal spelers"
                        ? (match.Groups["min"].Success ? "min" : "min2")
                        : (match.Groups["max"].Success ? "max" : "max2")].Value);
                foreach (Match match in PlayerCount.Matches(content))
                    Add(match.Groups[fact == "minimumaantal spelers"
                        ? (match.Groups["min"].Success ? "min" : "min2")
                        : (match.Groups["max"].Success ? "max" : "max2")].Value);
                foreach (Match match in (fact == "minimumaantal spelers" ? Minimum : Maximum).Matches(content))
                    Add(match.Groups["value"].Value);
            }
            else if (fact == "speelduur")
            {
                // Het datamodel heeft één geschatte duur; bij een bronbereik gebruiken we de bovengrens.
                foreach (Match match in DurationLabel.Matches(content))
                    Add(match.Groups[match.Groups["max"].Success ? "max" : "min"].Value);
                foreach (Match match in DurationValue.Matches(content))
                {
                    var before = content[..match.Index];
                    var clause = before[(before.LastIndexOfAny(['.', '!', '?', '\n']) + 1)..];
                    if (Instructional.IsMatch(clause)) continue;
                    Add(match.Groups[match.Groups["max"].Success ? "max" : "min"].Value);
                }
            }
        }
        return values.Order().ToArray();

        void Add(string raw)
        {
            if (int.TryParse(raw, out var value) && value > 0) values.Add(value);
        }
    }

    public static bool HasFact(IReadOnlyList<GameSearchHit> hits, string fact)
    {
        return Resolve(hits, fact).Value.HasValue;
    }

    public static bool SupportsChosen(IReadOnlyList<GameSearchHit> hits, int value, string fact)
    {
        if (value <= 0) return false;
        var decision = Resolve(hits, fact);
        return decision.Value == value || decision.Value is null && decision.Candidates.Count == 0
            && hits.Any(hit => SupportsHit(hit, value, fact));
    }

    private static bool SupportsHit(GameSearchHit hit, int value, string fact)
    {
        if (RawValues([hit], fact).Contains(value)) return true;
        var number = $@"(?<!\d){value}(?!\d)";
        var topic = fact == "speelduur"
            ? @"(?:minutes?|minuten|min\.?|speelduur|speeltijd|playing\s*time|play\s*time|duration)"
            : @"(?:players?|spelers?|player\s*count|aantal\s+spelers)";
        foreach (Match match in Regex.Matches(hit.Content, number, RegexOptions.IgnoreCase))
        {
            var start = Math.Max(0, match.Index - 60);
            var end = Math.Min(hit.Content.Length, match.Index + match.Length + 60);
            var context = hit.Content[start..end];
            if (Regex.IsMatch(context, topic, RegexOptions.IgnoreCase)
                && (fact != "speelduur" || !Instructional.IsMatch(context))) return true;
        }
        return false;
    }

    public static GameSearchHit? PreferredSource(IReadOnlyList<GameSearchHit> hits, int value, string fact)
    {
        var decision = Resolve(hits, fact);
        if (decision.Value == value) return decision.Sources.FirstOrDefault();
        return decision.Value is null && decision.Candidates.Count == 0
            ? hits.Where(hit => SupportsHit(hit, value, fact)).OrderByDescending(Rank).FirstOrDefault()
            : null;
    }

    public static string Summary(IReadOnlyList<GameSearchHit> hits) => string.Join("; ",
        new[] { "minimumaantal spelers", "maximumaantal spelers", "speelduur" }.Select(fact =>
        {
            var decision = Resolve(hits, fact);
            return decision.Value is { } value
                ? $"{fact}={value} ({decision.Reason}, {decision.Sources[0].Url})"
                : $"{fact}=onbekend ({decision.Reason})";
        }));

    private static int Rank(GameSearchHit hit)
    {
        if (IsBgg(hit)) return Regex.IsMatch(new Uri(hit.Url).AbsolutePath,
            @"^/boardgame/\d+(?:/[^/]+)?/?$", RegexOptions.IgnoreCase) ? 4 : 3;
        if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return -1;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("publisher.") || Regex.IsMatch(hit.Title, @"\b(?:official\s+publisher|publisher|uitgever)\b", RegexOptions.IgnoreCase)
            || new[] { "hasbro.com", "amigo-spiele.de", "amigo.games", "ravensburger.com", "ravensburger.de",
                "999games.nl", "oinkgames.com", "asmodee.com" }.Any(domain => host == domain || host.EndsWith('.' + domain))) return 2;
        if (new[] { "boardgameatlas.com", "boardgameoracle.com", "ludopedia.com.br" }
            .Any(domain => host == domain || host.EndsWith('.' + domain))) return 1;
        return 0;
    }

    private static string TierName(int rank) => rank switch
    {
        4 => "BoardGameGeek", 3 => "BoardGameGeek-subpagina", 2 => "uitgever", 1 => "bordspellendatabase", _ => "overige bronnen"
    };

    private static int Strength(GameSearchHit hit, string fact)
    {
        if (fact == "speelduur") return DurationLabel.IsMatch(hit.Content) ? 3 : 1;
        return Regex.IsMatch(hit.Content, @"\b(?:players?|spelers?)\s*:|\b(?:minimum|maximum|minimaal|maximaal)\b",
            RegexOptions.IgnoreCase) ? 3 : 2;
    }

    public static string Context(IReadOnlyList<GameSearchHit> hits) => string.Join("\n", hits.Select(hit =>
        $"Bron: {hit.Title} ({hit.Url}) — {hit.Content[..Math.Min(hit.Content.Length, 900)]}"));

    private static bool IsBgg(GameSearchHit hit) => Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("boardgamegeek.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.boardgamegeek.com", StringComparison.OrdinalIgnoreCase))
        && Regex.IsMatch(uri.AbsolutePath, @"^/boardgame/\d+(?:/|$)", RegexOptions.IgnoreCase);
}

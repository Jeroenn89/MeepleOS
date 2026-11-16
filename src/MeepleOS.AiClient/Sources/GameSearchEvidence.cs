using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

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

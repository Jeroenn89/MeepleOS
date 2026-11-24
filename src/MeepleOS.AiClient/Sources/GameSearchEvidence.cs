using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

internal static class GameSearchEvidence
{
    private static readonly Regex PlayerRange = new(@"(?<!\d)(?<min>\d+)\s*(?:-|–|—|to|tot)\s*(?<max>\d+)\s*(?:players?|spelers?)\b|\b(?:players?|spelers?)\s*:?\s*(?<min2>\d+)\s*(?:-|–|—|to|tot)\s*(?<max2>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Minimum = new(@"\b(?:minimum|min(?:imum)?\.?|minimaal|vanaf|at[ \t]+least)[ \t]*:?[ \t]*(?:of[ \t]+)?(?<value>\d+)[ \t]*(?:players?|spelers?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Maximum = new(@"\b(?:maximum|max(?:imum)?\.?|maximaal)[ \t]*:?[ \t]*(?:of[ \t]+)?(?<value>\d+)[ \t]*(?:players?|spelers?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DurationLabel = new(@"\b(?:playing\s*time|play\s*time|playtime|duration|speelduur|speeltijd|game\s*time)\s*:?\s*(?<min>\d+)\s*(?:(?:-|–|—|to|tot)\s*(?<max>\d+))?\s*(?:minutes?|minuten|min\.?)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DurationValue = new(@"(?<![\d-])(?<min>\d+)\s*(?:(?:-|–|—|to|tot)\s*(?<max>\d+))?\s*(?:minutes?|minuten|min\.?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Instructional = new(@"\b(?:learn|how\s+to\s+play|tutorial|rules|uitleg|spelregels|video|review)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlayerCount = new(@"\b(?:players?|spelers?|player\s*count|aantal\s+spelers)\s*:?\s*(?<min>\d+)\s*(?:-|–|—|to|tot|t/m)\s*(?<max>\d+)\b|\b(?:for|voor|with|met)\s+(?<min2>\d+)\s*(?:-|–|—|to|tot|t/m)\s*(?<max2>\d+)\s*(?:players?|spelers?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FixedPlayerCount = new(@"(?<!\d)(?<value>\d+)[ \t]*(?:players?|spelers?)\b|\b(?:aantal[ \t]+spelers|player[ \t]+count|players?|spelers?)[ \t]*:?[ \t]*(?<value2>\d+)\b(?![ \t]*(?:-|\u2013|\u2014|minutes?|minuten|min\.?|speelduur|speeltijd))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WordPlayerCount = new(@"\b(?<value>een|één|twee|drie|vier|vijf|zes|zeven|acht|negen|tien|elf|twaalf|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)[ \t]+(?:spelers?|players?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NoFixedMaximum = new(@"\b(?:geen[ \t]+(?:vast[ \t]+)?maximum|no[ \t]+(?:fixed[ \t]+)?maximum)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            var content = HasProductFact(hit, fact) ? hit.ProductFacts! : hit.Content;
            if (fact is "minimumaantal spelers" or "maximumaantal spelers")
            {
                var ranges = PlayerRange.Matches(content).Cast<Match>()
                    .Where(match => IsPlayerRange(content, match)).ToArray();
                foreach (var match in ranges)
                    Add(match.Groups[fact == "minimumaantal spelers"
                        ? (match.Groups["min"].Success ? "min" : "min2")
                        : (match.Groups["max"].Success ? "max" : "max2")].Value);
                foreach (Match match in PlayerCount.Matches(content))
                {
                    if (!IsPlayerRange(content, match)) continue;
                    Add(match.Groups[fact == "minimumaantal spelers"
                        ? (match.Groups["min"].Success ? "min" : "min2")
                        : (match.Groups["max"].Success ? "max" : "max2")].Value);
                }
                foreach (Match match in FixedPlayerCount.Matches(content))
                {
                    var number = match.Groups[match.Groups["value"].Success ? "value" : "value2"];
                    if (ranges.Any(range => number.Index >= range.Index && number.Index < range.Index + range.Length)
                        || IsQualifiedCount(content, number.Index)
                        || fact == "maximumaantal spelers" && NoFixedMaximum.IsMatch(content)) continue;
                    Add(number.Value);
                }
                if (fact == "minimumaantal spelers" || !NoFixedMaximum.IsMatch(content))
                    foreach (Match match in WordPlayerCount.Matches(content))
                        Add(WordNumber(match.Groups["value"].Value));
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
        if (fact is "minimumaantal spelers" or "maximumaantal spelers") return false;
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

    private static bool IsPlayerRange(string content, Match match)
    {
        if (match.Value.Contains('\n') || match.Value.Contains('\r')) return false;
        if (!Regex.IsMatch(match.Value, @"^(?:aantal|player|spelers?)\b", RegexOptions.IgnoreCase)) return true;
        return !Regex.IsMatch(content[(match.Index + match.Length)..],
            @"^[ \t]*(?:minutes?|minuten|min\.?|speelduur|speeltijd)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsQualifiedCount(string content, int numberIndex)
    {
        var start = Math.Max(0, numberIndex - 32);
        return Regex.IsMatch(content[start..numberIndex],
            @"(?:\b(?:minimaal|minimum|vanaf|at[ \t]+least|maximum|maximaal|max(?:imum)?\.)[ \t]*:?[ \t]*)$",
            RegexOptions.IgnoreCase);
    }

    private static string WordNumber(string word) => word.ToLowerInvariant() switch
    {
        "een" or "één" or "one" => "1", "twee" or "two" => "2",
        "drie" or "three" => "3", "vier" or "four" => "4",
        "vijf" or "five" => "5", "zes" or "six" => "6",
        "zeven" or "seven" => "7", "acht" or "eight" => "8",
        "negen" or "nine" => "9", "tien" or "ten" => "10",
        "elf" or "eleven" => "11", "twaalf" or "twelve" => "12",
        _ => ""
    };

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
            @"^/boardgame/\d+(?:/[^/]+)?/?$", RegexOptions.IgnoreCase) ? 4 : 1;
        if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return -1;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("publisher.") || Regex.IsMatch(hit.Title, @"\b(?:official\s+publisher|publisher|uitgever)\b", RegexOptions.IgnoreCase)
            || new[] { "hasbro.com", "amigo-spiele.de", "amigo.games", "ravensburger.com", "ravensburger.de",
                "999games.nl", "oinkgames.com", "asmodee.com", "daysofwonder.com" }
                .Any(domain => host == domain || host.EndsWith('.' + domain))) return 3;
        if (new[] { "boardgameatlas.com", "boardgameoracle.com", "ludopedia.com.br" }
            .Any(domain => host == domain || host.EndsWith('.' + domain))) return 2;
        return 0;
    }

    public static bool IsTrustedSource(GameSearchHit hit) => Rank(hit) is 4 or 3 or 2;

    public static bool IsPublisherSource(GameSearchHit hit) => Rank(hit) == 3;

    private static string TierName(int rank) => rank switch
    {
        4 => "BoardGameGeek", 3 => "uitgever", 2 => "bordspellendatabase", 1 => "BoardGameGeek-subpagina", _ => "overige bronnen"
    };

    private static int Strength(GameSearchHit hit, string fact)
    {
        if (HasProductFact(hit, fact)) return 5;
        if (fact == "speelduur") return DurationLabel.IsMatch(hit.Content) ? 3 : 1;
        return Regex.IsMatch(hit.Content, @"\b(?:players?|spelers?)\s*:|\b(?:minimum|maximum|minimaal|maximaal)\b",
            RegexOptions.IgnoreCase) ? 3 : 2;
    }

    public static string Context(IReadOnlyList<GameSearchHit> hits) => string.Join("\n", hits.Take(4).Select(hit =>
        $"Bron: {hit.Title} ({hit.Url}); productspecificaties: {hit.ProductFacts ?? "niet afzonderlijk gevonden"}. "
        + $"Paginainhoud: {hit.Content[..Math.Min(hit.Content.Length, 5000)]}"));

    private static bool HasProductFact(GameSearchHit hit, string fact) => hit.ProductFacts is { } productFacts
        && productFacts.Contains(fact == "speelduur" ? "Speelduur:" : "Spelers:", StringComparison.Ordinal);

    private static bool IsBgg(GameSearchHit hit) => Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("boardgamegeek.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.boardgamegeek.com", StringComparison.OrdinalIgnoreCase))
        && Regex.IsMatch(uri.AbsolutePath, @"^/boardgame/\d+(?:/|$)", RegexOptions.IgnoreCase);
}

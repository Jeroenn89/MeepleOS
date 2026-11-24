using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

internal static class GamePageSelector
{
    private static readonly Regex Alias = new(@"\b(?:also\s+known\s+as|alternat(?:e|ive)\s+(?:names?|titles?)|alternatieve\s+(?:namen?|titels?)|ook\s+bekend\s+als)\s*:?\s*(?<name>[^.;+\r\n|]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static List<GameSearchHit> SelectBgg(IEnumerable<GameSearchHit> results, string game)
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

    internal static bool IsCanonicalBgg(GameSearchHit hit) =>
        BggId(hit).HasValue && Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
        && Regex.IsMatch(uri.AbsolutePath, @"^/boardgame/\d+(?:/[^/]+)?/?$", RegexOptions.IgnoreCase);

    internal static int BggScore(GameSearchHit hit, string game)
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

    internal static List<GameSearchHit> MergeHits(IEnumerable<GameSearchHit> hits) => hits
        .GroupBy(hit => hit.Url, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First() with
        {
            Content = string.Join(" ", group.Select(hit => hit.Content).Distinct(StringComparer.Ordinal)),
            ProductFacts = group.Select(hit => hit.ProductFacts).FirstOrDefault(facts => !string.IsNullOrWhiteSpace(facts))
        }).ToList();

    internal static bool IsBgg(GameSearchHit hit) => Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
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

    internal static bool IsMatchingGeneral(GameSearchHit hit, string game)
    {
        if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.AbsolutePath.Trim('/').Length == 0) return false;
        if (Regex.IsMatch(hit.Title + " " + uri.AbsolutePath, @"\b(?:expansion|uitbreiding)\b", RegexOptions.IgnoreCase)) return false;
        return GameTitleMatcher.SearchScore(game, GameTitleMatcher.PageTitle(hit.Title)).HasValue
            || GameTitleMatcher.SearchScore(game, Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').Last())).HasValue
            || Alias.Matches(hit.Content).Any(match => GameTitleMatcher.SearchScore(game, match.Groups["name"].Value).HasValue);
    }

}

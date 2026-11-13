using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

internal static class BoardgameMention
{
    private static readonly Regex AfterCue = new(
        @"\b(?:speelde|registreer|registreren|bordspel|potje)\s+(?<name>[\p{L}\p{N}' -]{2,80}?)(?=\s+(?:met|tegen|vandaag|gisteren|op|gespeeld)\b|[.,;!?]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BeforePlayed = new(
        @"(?<name>[\p{L}\p{N}' -]{2,80}?)\s+gespeeld\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BeforeRegister = new(
        @"(?<name>[\p{L}\p{N}' -]{2,80}?)\s+registreren\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal sealed record Result(Item? Game, string? Mention, IReadOnlyList<Item> Ambiguous);

    public static Result Find(Catalog catalog, string command)
    {
        var phrases = Phrases(command)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (phrases.Length == 0)
            phrases = catalog.Boardgames.Where(game => Mentioned(command, game.Name))
                .Select(game => game.Name).ToArray();
        var scored = catalog.Boardgames.Select(game => new
        {
            Game = game,
            Matches = phrases.Select(phrase => (Phrase: phrase, Score: GameTitleMatcher.Score(phrase, game.Name)))
                .Where(candidate => candidate.Score.HasValue)
                .OrderBy(candidate => candidate.Score).ThenByDescending(candidate => candidate.Phrase.Length).ToArray()
        }).Where(candidate => candidate.Matches.Length > 0)
            .OrderBy(candidate => candidate.Matches[0].Score).ToArray();
        if (scored.Length == 0) return new Result(null, null, []);
        var best = scored[0].Matches[0].Score;
        var tied = scored.Where(candidate => candidate.Matches[0].Score == best).Select(candidate => candidate.Game).ToArray();
        return tied.Length == 1
            ? new Result(tied[0], scored[0].Matches[0].Phrase, [])
            : new Result(null, null, tied);
    }

    public static bool MatchesCommandName(string command, string name) =>
        Mentioned(command, name) || Phrases(command).Any(phrase => GameTitleMatcher.Score(phrase, name).HasValue);

    public static Result FindByName(Catalog catalog, string name)
    {
        var scored = catalog.Boardgames.Select(game => (Game: game, Score: GameTitleMatcher.Score(name, game.Name)))
            .Where(candidate => candidate.Score.HasValue).OrderBy(candidate => candidate.Score).ToArray();
        if (scored.Length == 0) return new Result(null, null, []);
        var tied = scored.Where(candidate => candidate.Score == scored[0].Score).Select(candidate => candidate.Game).ToArray();
        return tied.Length == 1 ? new Result(tied[0], name, []) : new Result(null, null, tied);
    }

    public static string CanonicalizeCommand(string command, Item game)
    {
        var mention = Find(new Catalog([game], [], []), command).Mention;
        return mention is null ? command : Regex.Replace(command,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(mention)}(?![\p{{L}}\p{{N}}])",
            game.Name, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static void ResolvePlanReferences(Plan plan, Item game)
    {
        foreach (var action in plan.Actions)
        {
            if (action.Kind == "add_boardgame"
                && (string.IsNullOrWhiteSpace(action.Name) || GameTitleMatcher.Score(action.Name, game.Name).HasValue))
                action.Name = game.Name;
            if (action.Kind == "record_session"
                && (string.IsNullOrWhiteSpace(action.Boardgame) || GameTitleMatcher.Score(action.Boardgame, game.Name).HasValue))
                action.Boardgame = game.Name;
        }
    }

    private static IEnumerable<string> Phrases(string command)
    {
        foreach (Match match in AfterCue.Matches(command))
            yield return match.Groups["name"].Value.Trim();
        foreach (var pattern in new[] { BeforePlayed, BeforeRegister })
            foreach (Match match in pattern.Matches(command))
            {
                var words = match.Groups["name"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var count = 1; count <= Math.Min(words.Length, 6); count++)
                    yield return string.Join(' ', words[^count..]);
            }
    }

    private static bool Mentioned(string command, string name) => Regex.IsMatch(command,
        $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

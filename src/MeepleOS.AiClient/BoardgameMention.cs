using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

internal static class BoardgameMention
{
    private static readonly Regex BeforePlayed = new(
        @"(?<token>[\p{L}\p{N}'-]{5,})\s+gespeeld\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Item? FindUnique(Catalog catalog, string command)
    {
        // The word immediately before "gespeeld" is the strongest cue for a short game name.
        var cue = BeforePlayed.Match(command);
        if (cue.Success)
        {
            var matched = MatchToken(catalog.Boardgames, cue.Groups["token"].Value);
            if (matched is not null)
                return matched;
        }

        var exact = catalog.Boardgames.Where(game => Mentioned(command, game.Name)).ToList();
        return exact.Count == 1 ? exact[0] : null;
    }

    public static string CanonicalizeCommand(string command, Item game) => BeforePlayed.Replace(command, match =>
    {
        var token = match.Groups["token"];
        return Similar(token.Value, game.Name)
            ? game.Name + match.Value[token.Length..]
            : match.Value;
    });

    public static void ResolvePlanReferences(Plan plan, Item game)
    {
        foreach (var action in plan.Actions)
        {
            if (action.Kind == "add_boardgame"
                && (string.IsNullOrWhiteSpace(action.Name) || Similar(action.Name, game.Name)))
                action.Name = game.Name;

            if (action.Kind == "record_session"
                && (string.IsNullOrWhiteSpace(action.Boardgame) || Similar(action.Boardgame, game.Name)))
                action.Boardgame = game.Name;
        }
    }

    private static Item? MatchToken(List<Item> games, string token)
    {
        var exact = games.Where(game => game.Name.Equals(token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
            return exact[0];

        var close = games.Where(game => !game.Name.Contains(' ')
                && game.Name.Length >= 5
                && EditDistanceAtMostOne(game.Name, token))
            .ToList();
        return close.Count == 1 ? close[0] : null;
    }

    private static bool Similar(string left, string right) =>
        left.Trim().Equals(right, StringComparison.OrdinalIgnoreCase)
        || !left.Contains(' ') && !right.Contains(' ') && EditDistanceAtMostOne(left.Trim(), right);

    private static bool Mentioned(string text, string name) =>
        Regex.IsMatch(text,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool EditDistanceAtMostOne(string left, string right)
    {
        left = left.ToUpperInvariant();
        right = right.ToUpperInvariant();
        if (Math.Abs(left.Length - right.Length) > 1)
            return false;

        var i = 0;
        var j = 0;
        var edits = 0;
        while (i < left.Length && j < right.Length)
        {
            if (left[i] == right[j])
            {
                i++;
                j++;
                continue;
            }

            if (++edits > 1)
                return false;
            if (left.Length >= right.Length)
                i++;
            if (right.Length >= left.Length)
                j++;
        }

        return edits + (left.Length - i) + (right.Length - j) <= 1;
    }
}

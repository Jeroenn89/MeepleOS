using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

internal static class GameTitleMatcher
{
    private static readonly HashSet<string> Ignored = ["the", "de", "het", "of", "van", "naar", "for", "a", "an"];

    public static int? Score(string? query, string? title)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(title)) return null;
        var wanted = Tokens(query, ignoreWords: true);
        var actual = Tokens(title, ignoreWords: true);
        if (wanted.Length == 0 || actual.Length == 0) return null;
        if (wanted.Length == 1 && wanted[0].Length >= 3)
        {
            var raw = Tokens(title, ignoreWords: false);
            if (wanted[0] == string.Concat(raw.Select(word => word[0]))) return 2;
        }
        if (wanted.Length > actual.Length) return null;

        var best = int.MaxValue;
        void Compare(int queryIndex, int titleIndex, int cost)
        {
            if (cost >= best) return;
            if (queryIndex == wanted.Length)
            {
                // Een toevoeging achter de naam duidt vaak een ander spel aan (Junior, kaartspel).
                best = Math.Min(best, cost + 3 * (actual.Length - titleIndex));
                return;
            }
            if (titleIndex == actual.Length) return;
            Compare(queryIndex, titleIndex + 1, cost + 2);
            var distance = EditDistance(wanted[queryIndex], actual[titleIndex]);
            if (distance <= 1)
                Compare(queryIndex + 1, titleIndex + 1, cost + distance);
        }
        Compare(0, 0, 0);
        return best <= Math.Max(2, wanted.Length) ? best : null;
    }

    public static string PageTitle(string title) =>
        Regex.Split(title, @"\s+[|–-]\s+|\s*\(\d{4}\)")[0].Trim();

    public static string[] Tokens(string value, bool ignoreWords)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                normalized.Append(character);
        return Regex.Matches(normalized.ToString(), @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .Where(word => !ignoreWords || !Ignored.Contains(word))
            .Select(word => word.Length > 4 && word.EndsWith('s') ? word[..^1] : word)
            .ToArray();
    }

    private static int EditDistance(string left, string right)
    {
        if (left == right) return 0;
        if (Math.Abs(left.Length - right.Length) > 1) return 2;
        if (left.Length == right.Length)
        {
            var changed = Enumerable.Range(0, left.Length).Where(index => left[index] != right[index]).ToArray();
            if (changed.Length == 2 && changed[1] == changed[0] + 1
                && left[changed[0]] == right[changed[1]] && left[changed[1]] == right[changed[0]])
                return 1;
        }
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var row = 1; row <= left.Length; row++)
        {
            var current = new int[right.Length + 1];
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
                current[column] = Math.Min(Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            previous = current;
        }
        return previous[^1];
    }
}

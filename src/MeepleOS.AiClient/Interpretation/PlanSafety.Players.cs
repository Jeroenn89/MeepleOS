using System.Text.RegularExpressions;
using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.AiClient;

internal static partial class PlanSafety
{
    private static void AddPlayer(string? name, string command, HashSet<string> players,
        List<ActionPlan> prepared, bool participant = false)
    {
        name = CanonicalPlayer(name);
        if (string.IsNullOrWhiteSpace(name))
            throw new ClarificationNeededException("Welke speler bedoel je?");
        var expanded = ExpandKnownPlayers(name, players);
        if (expanded.Count > 1)
            return;
        if (players.Contains(name))
            return;
        if (!Mentioned(command, name))
            throw new ClarificationNeededException($"Welke speler bedoel je met {name}?");
        if (participant && !ClearlyParticipant(command, name))
            throw new ClarificationNeededException($"Deed {name} mee aan het potje?");

        prepared.Add(new ActionPlan { Kind = "add_player", Name = name });
        players.Add(name);
    }

    private static List<string> ExpandKnownPlayers(string? rawName, HashSet<string> players)
    {
        var name = CanonicalPlayer(rawName);
        if (players.Contains(name) || !name.Contains(' '))
            return [name];

        var matches = new List<List<string>>();
        void Search(string remaining, List<string> selected)
        {
            if (matches.Count > 1)
                return;
            if (remaining.Length == 0)
            {
                if (selected.Count > 1)
                    matches.Add(selected);
                return;
            }

            foreach (var known in players)
            {
                if (!remaining.StartsWith(known, StringComparison.OrdinalIgnoreCase)
                    || remaining.Length > known.Length && !char.IsWhiteSpace(remaining[known.Length]))
                    continue;

                Search(remaining[known.Length..].TrimStart(), [.. selected, known]);
            }
        }

        Search(name, []);
        if (matches.Count == 1)
            return matches[0];
        if (matches.Count > 1 || players.Any(known => Mentioned(name, known)))
            throw new ClarificationNeededException($"Bedoel je met {name} één speler of meerdere spelers?");
        return [name];
    }

    private static bool Mentioned(string command, string name) =>
        Regex.IsMatch(command, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name.Trim())}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ClearlyParticipant(string command, string name)
    {
        foreach (Match match in Regex.Matches(command, @"\bmet\s+(?<list>[^.;\r\n]+)", RegexOptions.IgnoreCase))
        {
            var list = match.Groups["list"].Value;
            var nameMatch = Regex.Match(list,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (nameMatch.Success && !Regex.IsMatch(list[..nameMatch.Index], @"\buitbreiding\b", RegexOptions.IgnoreCase))
                return true;
        }

        return Regex.IsMatch(command,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}\s+(?:speelde|speelden|deed mee|speelde mee|was deelnemer)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string CanonicalPlayer(string? name)
    {
        var value = name?.Trim() ?? "";
        return value.Equals("ik", StringComparison.OrdinalIgnoreCase)
            || value.Equals("mij", StringComparison.OrdinalIgnoreCase)
            || value.Equals("mezelf", StringComparison.OrdinalIgnoreCase)
            ? "Jeroen" : value;
    }
}

using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

public sealed partial class CommandProcessor
{
    private static string Show(int? value) => value?.ToString() ?? "ontbreekt of conflicteert";

    private static bool ContainsGameName(string plannedName, string requestedName) =>
        Regex.IsMatch(plannedName, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(requestedName)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void TraceCandidate(string fact, int? value,
        IReadOnlyList<GameSearchHit> hits, Action<string>? onTrace)
    {
        if (value is null) { onTrace?.Invoke($"Gemini-waarde {fact} ontbreekt."); return; }
        var supporting = GameSearchEvidence.PreferredSource(hits, value.Value, fact) is { } source
            ? new[] { source.Url } : [];
        onTrace?.Invoke(supporting.Length == 0
            ? $"Gemini-waarde {fact}={value} heeft geen passende Tavily-bron en kan bij de broncontrole worden afgewezen."
            : $"Gemini-waarde {fact}={value} wordt gesteund door: {string.Join(", ", supporting)}.");
    }

    private static void TraceGamePlan(string stage, Plan plan, Action<string>? onTrace)
    {
        if (onTrace is null) return;
        var games = (plan.Actions ?? []).Where(action => action.Kind == "add_boardgame").ToList();
        if (games.Count == 0)
        {
            onTrace($"{stage}: geen add_boardgame-actie; vraag aanwezig={!string.IsNullOrWhiteSpace(plan.Question)}.");
            return;
        }
        foreach (var game in games)
            onTrace($"{stage} voor {game.Name}: minimumspelers={Show(game.MinPlayers)}, "
                + $"maximumspelers={Show(game.MaxPlayers)}, geen vast maximum={game.NoMaximum}, "
                + $"speelduur={Show(game.EstimatedMinutes)} minuten; vraag aanwezig={!string.IsNullOrWhiteSpace(plan.Question)}.");
    }

    private static string FormatOnlineFacts(ActionPlan action, string command, IReadOnlyList<GameSearchHit> hits)
    {
        var missing = PlanSafety.MissingGameFacts(command);
        var sourced = new List<(string Fact, GameSearchHit Hit)>();
        void Add(string key, int? value)
        {
            if (!missing.Contains(key) || value is null) return;
            var hit = GameSearchEvidence.PreferredSource(hits, value.Value, key);
            if (hit is not null) sourced.Add((key, hit));
        }
        Add("minimumaantal spelers", action.MinPlayers);
        Add("maximumaantal spelers", action.MaxPlayers);
        Add("speelduur", action.EstimatedMinutes);
        if (sourced.Count == 0) return "";

        var lines = new List<string>();
        var min = sourced.Any(fact => fact.Fact == "minimumaantal spelers");
        var max = sourced.Any(fact => fact.Fact == "maximumaantal spelers");
        if (min && max) lines.Add($"Spelers: {action.MinPlayers}–{action.MaxPlayers}");
        else if (min) lines.Add($"Minimumspelers: {action.MinPlayers}");
        else if (max) lines.Add($"Maximumspelers: {action.MaxPlayers}");
        if (sourced.Any(fact => fact.Fact == "speelduur"))
            lines.Add($"Speelduur: {action.EstimatedMinutes} minuten");

        var sources = sourced.GroupBy(fact => fact.Hit.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var uri = new Uri(group.Key);
                var label = uri.Host.Contains("boardgamegeek.com", StringComparison.OrdinalIgnoreCase)
                    ? "BoardGameGeek" : uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                        ? uri.Host[4..] : uri.Host;
                var facts = group.Select(fact => fact.Fact == "speelduur" ? "speelduur" : "spelers")
                    .Distinct().ToArray();
                return $"[{label}]({group.Key}) ({string.Join(", ", facts)})";
            }).ToArray();
        lines.Add($"{(sources.Length == 1 ? "Bron" : "Bronnen")}: {string.Join("; ", sources)}");
        return Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}

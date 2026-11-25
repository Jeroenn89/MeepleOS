using System.Text.RegularExpressions;
using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.AiClient;

internal static partial class PlanSafety
{
    private static void AddBoardgame(string? name, string command, HashSet<string> games, List<ActionPlan> prepared,
        IReadOnlyList<GameSearchHit>? searchHits = null, Action<string>? onTrace = null, string? verifiedGameName = null,
        ActionPlan? proposed = null)
    {
        if (!string.IsNullOrWhiteSpace(name) && games.Contains(name))
            return;
        if (string.IsNullOrWhiteSpace(name) || !BoardgameMention.MatchesCommandName(command, name)
            && !Names.Equals(name, verifiedGameName))
        {
            onTrace?.Invoke($"Bordspelnaam afgewezen: geplande naam '{name ?? "(leeg)"}' ontbreekt in de opdracht.");
            throw new ClarificationNeededException("Welk bordspel bedoel je?");
        }

        var userFacts = UserGameFacts(command);
        var minimum = userFacts.MinPlayers;
        var maximum = userFacts.MaxPlayers;
        var noMaximum = userFacts.NoMaximum;
        var minutes = userFacts.Minutes;

        if (searchHits is { Count: > 0 })
        {
            var facts = GameSearchEvidence.Extract(searchHits);
            foreach (var fact in new[] { "minimumaantal spelers", "maximumaantal spelers", "speelduur" })
            {
                var decision = GameSearchEvidence.Resolve(searchHits, fact);
                onTrace?.Invoke($"Broncontrole {name}, {fact}: {decision.Reason}; "
                    + (decision.Value is { } value ? $"waarde {value}" : $"waarden {string.Join(", ", decision.Candidates)}") + ".");
            }
            minimum ??= facts.MinPlayers;
            if (!noMaximum) maximum ??= facts.MaxPlayers;
            minutes ??= facts.EstimatedMinutes;
            if (proposed is not null)
            {
                if (minimum is null && proposed.MinPlayers is { } min
                    && GameSearchEvidence.SupportsChosen(searchHits, min, "minimumaantal spelers")) minimum = min;
                if (!noMaximum && maximum is null && proposed.MaxPlayers is { } max
                    && GameSearchEvidence.SupportsChosen(searchHits, max, "maximumaantal spelers")) maximum = max;
                if (minutes is null && proposed.EstimatedMinutes is { } duration
                    && GameSearchEvidence.SupportsChosen(searchHits, duration, "speelduur")) minutes = duration;
            }
        }

        if (noMaximum && maximum.HasValue)
            throw new ClarificationNeededException($"Heeft {name} een maximumaantal spelers of geen vast maximum?");

        var missing = new List<string>();
        if (minimum is null or < 1) missing.Add("minimumaantal spelers");
        if (!noMaximum && maximum is null) missing.Add("maximumaantal spelers (of geen vast maximum)");
        if (minutes is null or < 1) missing.Add("speelduur in minuten");
        if (missing.Count > 0)
        {
            foreach (var fact in missing) onTrace?.Invoke($"Bordspelgegeven afgewezen voor {name}: {fact} ontbreekt na opdracht en broncontrole.");
            throw new ClarificationNeededException($"Voor {name} ontbreken nog {string.Join(", ", missing)}. Wat zijn die gegevens?");
        }
        if (!noMaximum && maximum < minimum)
            throw new ClarificationNeededException($"Wat is het juiste maximumaantal spelers voor {name}?");

        prepared.Add(new ActionPlan
        {
            Kind = "add_boardgame",
            Name = name.Trim(),
            MinPlayers = minimum,
            MaxPlayers = noMaximum ? null : maximum,
            NoMaximum = noMaximum,
            EstimatedMinutes = minutes
        });
        games.Add(name);
    }

    private static void AddExpansion(
        string? name, string? game, string command, HashSet<string> games,
        List<(string? Game, string Name)> expansions, List<ActionPlan> prepared)
    {
        if (string.IsNullOrWhiteSpace(game) || !games.Contains(game))
            throw new ClarificationNeededException("Bij welk basisspel hoort de uitbreiding?");
        if (string.IsNullOrWhiteSpace(name) || !Mentioned(command, name))
            throw new ClarificationNeededException("Welke uitbreiding bedoel je?");
        if (HasExpansion(expansions, game, name))
            return;

        prepared.Add(new ActionPlan { Kind = "add_expansion", Name = name.Trim(), Boardgame = game });
        expansions.Add((game, name));
    }

    private static bool HasExpansion(List<(string? Game, string Name)> expansions, string game, string name) =>
        expansions.Any(expansion => Names.Equals(expansion.Game, game) && Names.Equals(expansion.Name, name));

}

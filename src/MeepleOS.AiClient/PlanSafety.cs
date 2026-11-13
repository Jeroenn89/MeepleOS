using System.Text.RegularExpressions;
using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.AiClient;

internal sealed class ClarificationNeededException(string question, IReadOnlyList<GameSearchHit>? choices = null)
    : Exception(question)
{
    public IReadOnlyList<GameSearchHit> Choices { get; } = choices ?? [];
}

internal static class PlanSafety
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex PlayerRange = new(
        @"(?<!\d)(?<min>\d+)\s*(?:-|–|—|to|tot|t/m)\s*(?<max>\d+)\s*(?:players?|spelers?)\b|\b(?:players?|spelers?)\s*:?\s*(?<min2>\d+)\s*(?:-|–|—|to|tot|t/m)\s*(?<max2>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Minimum = new(
        @"\b(?:minimaal|minimum|min\.?)\s*(?:of\s*)?(?<value>\d+)\s*(?:players?|spelers?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Maximum = new(
        @"\b(?:maximaal|maximum|max\.?)\s*(?:of\s*)?(?<value>\d+)\s*(?:players?|spelers?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Duration = new(
        @"\b(?<value>\d+)\s*(?:minutes?|minuten|min\.?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NoMaximum = new(
        @"\b(?:geen vast maximum|zonder vast maximum|onbeperkt aantal spelers)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OrderedScores = new(
        @"\bscores?\b[^.;\r\n]*?\b(?:in die volgorde|respectievelijk)\s+(?<scores>-?\d+(?:\s*[,/]\s*|\s+en\s+)-?\d+[^.;\r\n]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool NeedsGameFacts(string command)
        => MissingGameFacts(command).Count > 0;

    public static IReadOnlyList<string> MissingGameFacts(string command)
    {
        var range = PlayerRange.Match(command);
        var missing = new List<string>();
        if (!(range.Success || ReadNumber(Minimum, command).HasValue)) missing.Add("minimumaantal spelers");
        if (!(range.Success || ReadNumber(Maximum, command).HasValue || NoMaximum.IsMatch(command))) missing.Add("maximumaantal spelers");
        if (!ReadNumber(Duration, command).HasValue) missing.Add("speelduur");
        return missing;
    }

    public static Plan Prepare(Plan plan, Catalog catalog, string command, bool readOnly,
        IReadOnlyList<GameSearchHit>? searchHits = null, Action<string>? onTrace = null, string? verifiedGameName = null)
    {
        if (readOnly || plan.Actions is null)
            return plan;

        var games = catalog.Boardgames.Select(item => item.Name).ToHashSet(Names);
        var players = catalog.Players.Select(item => item.Name).ToHashSet(Names);
        var expansions = catalog.Expansions
            .Select(item => (Game: catalog.Boardgames.FirstOrDefault(game => game.Id == item.BoardGameId)?.Name,
                             Name: item.Name))
            .ToList();
        var prepared = new List<ActionPlan>();

        foreach (var action in plan.Actions)
        {
            switch (action.Kind)
            {
                case "add_player":
                    var isParticipant = plan.Actions.Any(candidate => candidate.Kind == "record_session"
                        && candidate.Players.Any(player => Names.Equals(CanonicalPlayer(player.Name), CanonicalPlayer(action.Name))));
                    if (!isParticipant && !Regex.IsMatch(command,
                            @"\b(?:voeg|registreer|maak)\b[^.;\r\n]*\bspeler\b", RegexOptions.IgnoreCase))
                        throw new ClarificationNeededException($"Wil je {action.Name} als speler toevoegen?");
                    AddPlayer(action.Name, command, players, prepared, isParticipant);
                    break;

                case "add_boardgame":
                    AddBoardgame(action.Name, command, games, prepared, searchHits, onTrace, verifiedGameName, action);
                    break;

                case "add_expansion":
                    AddExpansion(action.Name, action.Boardgame, command, games, expansions, prepared);
                    break;

                case "record_session":
                    PrepareSession(action, command, games, players, expansions, prepared, searchHits, onTrace, verifiedGameName);
                    prepared.Add(action);
                    break;

                default:
                    prepared.Add(action);
                    break;
            }
        }

        plan.Actions = prepared;
        return plan;
    }

    private static void PrepareSession(
        ActionPlan session, string command, HashSet<string> games, HashSet<string> players,
        List<(string? Game, string Name)> expansions, List<ActionPlan> prepared,
        IReadOnlyList<GameSearchHit>? searchHits, Action<string>? onTrace, string? verifiedGameName)
    {
        if (string.IsNullOrWhiteSpace(session.Boardgame))
            throw new ClarificationNeededException("Welk bordspel heb je gespeeld?");

        if (!games.Contains(session.Boardgame))
            AddBoardgame(session.Boardgame, command, games, prepared, searchHits: searchHits,
                onTrace: onTrace, verifiedGameName: verifiedGameName);

        var participants = new List<ParticipantPlan>();
        foreach (var plannedParticipant in session.Players)
        {
            var expandedNames = ExpandKnownPlayers(plannedParticipant.Name, players);
            if (expandedNames.Count > 1 && (plannedParticipant.Score.HasValue || plannedParticipant.Position.HasValue))
                throw new ClarificationNeededException($"Bij welke deelnemer horen de score en positie van {plannedParticipant.Name}?");

            foreach (var name in expandedNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new ClarificationNeededException("Welke speler deed mee?");

                var duplicate = participants.FirstOrDefault(existing => Names.Equals(existing.Name, name));
                if (duplicate is not null)
                {
                    if (duplicate.Score.HasValue && plannedParticipant.Score.HasValue && duplicate.Score != plannedParticipant.Score
                        || duplicate.Position.HasValue && plannedParticipant.Position.HasValue && duplicate.Position != plannedParticipant.Position)
                        throw new ClarificationNeededException($"Welke score en positie horen bij {name}?");

                    duplicate.Score ??= plannedParticipant.Score;
                    duplicate.Position ??= plannedParticipant.Position;
                    continue;
                }

                if (!players.Contains(name))
                    AddPlayer(name, command, players, prepared, participant: true);

                participants.Add(new ParticipantPlan
                {
                    Name = name,
                    Score = plannedParticipant.Score,
                    Position = plannedParticipant.Position
                });
            }
        }
        session.Players = participants;
        if (Regex.IsMatch(command, @"\bik\s+(?:heb\b[^.;\r\n]*\bgespeeld|speelde)\b", RegexOptions.IgnoreCase)
            && players.Contains("Jeroen") && !participants.Any(p => Names.Equals(p.Name, "Jeroen")))
            participants.Insert(0, new ParticipantPlan { Name = "Jeroen" });

        var ordered = OrderedScores.Match(command);
        if (ordered.Success && participants.All(p => !p.Score.HasValue))
        {
            var values = Regex.Matches(ordered.Groups["scores"].Value, @"-?\d+")
                .Select(match => int.TryParse(match.Value, out var score) ? (int?)score : null).ToArray();
            if (values.Length == participants.Count && values.All(value => value.HasValue))
                for (var index = 0; index < participants.Count; index++) participants[index].Score = values[index];
        }
        session.Winners = session.Winners
            .SelectMany(winner => ExpandKnownPlayers(winner, players))
            .Distinct(Names)
            .ToList();
        var explicitlyNamed = participants.Where(participant =>
            Regex.IsMatch(command, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(participant.Name)}\s+(?:won|heeft\s+gewonnen)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Names.Equals(participant.Name, "Jeroen") && Regex.IsMatch(command,
                @"\bik\s+(?:won|heb\s+gewonnen)\b", RegexOptions.IgnoreCase))
            .Select(participant => participant.Name).ToList();
        if (explicitlyNamed.Count == 1) session.Winners = explicitlyNamed;
        if (session.Winners.Count == 0 && participants.Count > 0 && participants.All(p => p.Score.HasValue))
        {
            var derived = SessionWinnerResolver.Resolve(
                participants.Select((p, index) => new SessionPlayerInput(index + 1, p.Score, p.Position)).ToArray(), null);
            session.Winners = derived.Select(id => participants[id - 1].Name).ToList();
        }
        if (session.Winners.Count == 0)
            throw new ClarificationNeededException("Wie heeft gewonnen?");

        foreach (var expansion in session.Expansions.Distinct(Names))
        {
            if (!HasExpansion(expansions, session.Boardgame, expansion))
                AddExpansion(expansion, session.Boardgame, command, games, expansions, prepared);
        }
        session.Expansions = session.Expansions.Distinct(Names).ToList();
    }

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

        var range = PlayerRange.Match(command);
        var minimum = range.Success ? int.Parse(range.Groups[range.Groups["min"].Success ? "min" : "min2"].Value) : ReadNumber(Minimum, command);
        var maximum = range.Success ? int.Parse(range.Groups[range.Groups["max"].Success ? "max" : "max2"].Value) : ReadNumber(Maximum, command);
        var noMaximum = NoMaximum.IsMatch(command);
        var minutes = ReadNumber(Duration, command);

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

    private static int? ReadNumber(Regex pattern, string command)
    {
        var match = pattern.Match(command);
        return match.Success && int.TryParse(match.Groups["value"].Value, out var value) ? value : null;
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

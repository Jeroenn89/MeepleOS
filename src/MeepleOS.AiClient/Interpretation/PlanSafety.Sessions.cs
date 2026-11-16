using System.Text.RegularExpressions;
using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.AiClient;

internal static partial class PlanSafety
{
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

}

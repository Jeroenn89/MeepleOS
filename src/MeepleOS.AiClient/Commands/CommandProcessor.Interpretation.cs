using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

public sealed partial class CommandProcessor
{
    private async Task<(Plan Plan, string PlannerCommand, GameSearchHit? ChosenHit, bool RejectedChoices)>
        InterpretAsync(string command, string fullCommand, Catalog catalog, DateOnly today, CancellationToken ct)
    {
        var answeringGameQuestion = PendingQuestion is not null && BoardgameQuestion.IsMatch(PendingQuestion);
        var commandMatch = BoardgameMention.Find(catalog, answeringGameQuestion ? command : fullCommand);
        var knownGame = commandMatch.Game;
        onTrace?.Invoke($"Bestaand spel: {knownGame?.Name ?? "geen"}.");
        var plannerCommand = knownGame is null ? fullCommand
            : $"{BoardgameMention.CanonicalizeCommand(fullCommand, knownGame)}{Environment.NewLine}Bestaand bordspel uit de catalogus: {knownGame.Name}. Gebruik exact deze naam en vraag niet welk spel bedoeld is.";
        if (commandMatch.Ambiguous.Count > 0)
            plannerCommand += $"{Environment.NewLine}Mogelijke bestaande spellen: {string.Join(", ", commandMatch.Ambiguous.Select(game => game.Name))}. Kies alleen als de oorspronkelijke opdracht het spel ondubbelzinnig aanwijst.";
        if (PendingQuestion is not null)
            plannerCommand += $"{Environment.NewLine}Vorige vraag van MeepleOS: {PendingQuestion}{Environment.NewLine}Laatste antwoord van gebruiker: {command}";
        if (PendingChoices.Count > 0)
            plannerCommand += $"{Environment.NewLine}Aangeboden spelopties: {string.Join("; ", PendingChoices.Select((hit, index) => $"{index + 1}. {hit.Title} (BGG-ID {TavilyGameSearch.BggId(hit)}; {hit.Content[..Math.Min(hit.Content.Length, 180)]})"))}. Kies de bedoelde optie op grond van het laatste antwoord. Als geen optie klopt, zet gameChoice op 'geen'; de client zoekt dan opnieuw.";
        var plan = await planner.PlanAsync(plannerCommand, catalog, today, ct);
        if (knownGame is null && !string.IsNullOrWhiteSpace(plan.AlternativeGameName))
        {
            var existing = catalog.Boardgames.FirstOrDefault(game =>
                GameTitleMatcher.SameTitle(game.Name, plan.AlternativeGameName));
            if (existing is not null && plan.Actions is { } actions
                && actions.Any(action => action.Kind is "add_boardgame" or "record_session"))
            {
                foreach (var action in actions)
                {
                    if (action.Kind == "add_boardgame" && BoardgameMention.MatchesCommandName(fullCommand, action.Name ?? ""))
                        action.Name = existing.Name;
                    if (action.Kind == "record_session" && BoardgameMention.MatchesCommandName(fullCommand, action.Boardgame ?? ""))
                        action.Boardgame = existing.Name;
                }
                knownGame = existing;
                onTrace?.Invoke($"Alternatieve titel staat al in de collectie: {existing.Name}.");
            }
        }
        var chosenHit = PendingChoices.FirstOrDefault(hit =>
            TavilyGameSearch.BggId(hit)?.ToString() == plan.GameChoice);
        if (chosenHit is null && !string.IsNullOrWhiteSpace(plan.GameChoice))
        {
            var byTitle = PendingChoices.Where(hit =>
                GameTitleMatcher.PageTitle(hit.Title).Equals(plan.GameChoice, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (byTitle.Length == 1) chosenHit = byTitle[0];
        }
        if (chosenHit is null && plan.GameChoice is null && PendingChoices.Count > 0)
        {
            var ordinal = Regex.Match(command, @"\b(?:de\s+)?(?<n>eerste|tweede|derde|vierde)\b", RegexOptions.IgnoreCase);
            var index = ordinal.Success ? ordinal.Groups["n"].Value.ToLowerInvariant() switch
                { "eerste" => 0, "tweede" => 1, "derde" => 2, "vierde" => 3, _ => -1 } : -1;
            if (index >= 0 && index < PendingChoices.Count) chosenHit = PendingChoices[index];
        }
        var rejectedChoices = PendingQuestion is not null
            && plan.GameChoice?.Equals("geen", StringComparison.OrdinalIgnoreCase) == true;
        if (rejectedChoices)
            RejectedBggIds = RejectedBggIds.Concat(PendingChoices.Select(TavilyGameSearch.BggId)
                .Where(id => id.HasValue).Select(id => id!.Value)).Distinct().ToArray();
        if (knownGame is null)
        {
            var plannedName = (plan.Actions ?? []).Select(action => action.Kind switch
                {
                    "add_boardgame" => action.Name,
                    "record_session" => action.Boardgame,
                    "update_boardgame" or "delete_boardgame" => catalog.Boardgames
                        .FirstOrDefault(game => game.Id == action.Id)?.Name,
                    _ => null
                })
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            if (plannedName is not null)
            {
                var plannedMatch = BoardgameMention.FindByName(catalog, plannedName);
                if (commandMatch.Ambiguous.Count > 0 && !answeringGameQuestion
                    && (plannedMatch.Game is null || !Regex.IsMatch(fullCommand,
                        $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(plannedMatch.Game.Name)}(?![\p{{L}}\p{{N}}])",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                    throw new ClarificationNeededException($"Welk bordspel bedoel je: {string.Join(" of ", commandMatch.Ambiguous.Select(game => game.Name))}?");
                if (plannedMatch.Game is not null && !answeringGameQuestion
                    && !BoardgameMention.MatchesCommandName(fullCommand, plannedName))
                    throw new ClarificationNeededException("Welk bordspel bedoel je?");
                knownGame = plannedMatch.Game;
            }
        }
        if (knownGame is not null && !string.IsNullOrWhiteSpace(plan.Question)
            && BoardgameQuestion.IsMatch(plan.Question))
        {
            plan = await planner.PlanAsync(
                $"{plannerCommand}{Environment.NewLine}Verduidelijking: het bedoelde bordspel is {knownGame.Name}. Maak nu het volledige plan voor de oorspronkelijke opdracht.",
                catalog, today, ct);
        }
        if (knownGame is not null && plan.Actions is not null)
            BoardgameMention.ResolvePlanReferences(plan, knownGame);
        return (plan, plannerCommand, chosenHit, rejectedChoices);
    }
}

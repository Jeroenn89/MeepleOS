using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

public sealed partial class CommandProcessor
{
    private async Task<(Plan Plan, IReadOnlyList<GameSearchHit> Hits, string? VerifiedGameName, CommandResult? EarlyResult)>
        ResearchGameAsync(string command, string fullCommand, string plannerCommand, Plan plan, Catalog catalog,
            GameSearchHit? chosenHit, bool rejectedChoices, DateOnly today, int countBefore, CancellationToken ct)
    {
        var originalPlan = plan;
        IReadOnlyList<GameSearchHit> searchHits = [];
        string? verifiedGameName = null;
        string? verifiedRequestedName = null;
        if (!readOnly && gameSearch is not null && (PlanSafety.NeedsGameFacts(fullCommand) || rejectedChoices))
        {
            var newGame = FindNewGameName(command, fullCommand, plan, catalog, chosenHit, rejectedChoices);
            if (newGame is not null)
            {
                PendingSearchName = newGame;
                IReadOnlyList<string> missing = PlanSafety.MissingGameFacts(fullCommand);
                if (rejectedChoices && missing.Count == 0) missing = ["speelduur"];
                onTrace?.Invoke($"Tavily zoekt voor {newGame}: {string.Join(", ", missing)}.");
                if (gameSearch is TavilyGameSearch { IsConfigured: false })
                    onTrace?.Invoke("Tavily overgeslagen: TAVILY_API_KEY ontbreekt.");
                searchHits = await gameSearch.SearchAsync(newGame, plan.AlternativeGameName, missing,
                    chosenHit is null ? null : TavilyGameSearch.BggId(chosenHit), RejectedBggIds, ct);
                if (rejectedChoices && searchHits.Count == 0)
                {
                    PendingQuestion = $"De andere zoekresultaten voor {newGame} geven nog geen passend spel. Welke titel of editie bedoel je?";
                    PendingChoices = [];
                    PendingCommand = fullCommand;
                    return (plan, searchHits, verifiedGameName, new CommandResult(PendingQuestion, (planner as IRequestCounter)?.RequestCount - countBefore ?? 1));
                }
                var alternativeName = plan.AlternativeGameName;
                var translatedSource = !string.IsNullOrWhiteSpace(alternativeName)
                    && !alternativeName.Equals(newGame, StringComparison.OrdinalIgnoreCase)
                    && searchHits.Any(hit => TavilyGameSearch.IsMatchingBgg(hit, alternativeName)
                        || GamePageSelector.IsMatchingGeneral(hit, alternativeName));
                var matchingBgg = searchHits.Where(hit => TavilyGameSearch.IsMatchingBgg(hit, newGame)
                    || translatedSource && TavilyGameSearch.IsMatchingBgg(hit, alternativeName!)
                    || chosenHit is not null && TavilyGameSearch.BggId(hit) == TavilyGameSearch.BggId(chosenHit)).ToArray();
                var bggHit = matchingBgg.FirstOrDefault(GamePageSelector.IsCanonicalBgg)
                    ?? matchingBgg.FirstOrDefault();
                var officialHit = searchHits.FirstOrDefault(hit => GameSearchEvidence.IsPublisherSource(hit)
                    && GamePageSelector.IsMatchingGeneral(hit, newGame)
                    && missing.All(fact => GameSearchEvidence.HasFact([hit], fact)));
                if (bggHit is not null && (GamePageSelector.IsCanonicalBgg(bggHit)
                    || officialHit is null || chosenHit is not null))
                {
                    var title = translatedSource ? newGame : chosenHit is null ? TavilyGameSearch.BggTitle(bggHit, newGame)
                        : GameTitleMatcher.PageTitle(bggHit.Title);
                    verifiedGameName = BoardgameMention.FindByName(catalog, title).Game?.Name ?? title;
                    verifiedRequestedName = newGame;
                    onTrace?.Invoke($"BGG-spelnaam voor {newGame}: {verifiedGameName}.");
                }
                else if (officialHit is not null)
                {
                    var title = GameTitleMatcher.PageTitle(officialHit.Title);
                    verifiedGameName = BoardgameMention.FindByName(catalog, title).Game?.Name ?? title;
                    verifiedRequestedName = newGame;
                    onTrace?.Invoke($"Bron bevestigt spelnaam voor {newGame}: {verifiedGameName}.");
                }
                if (gameSearch is not TavilyGameSearch { IsConfigured: false })
                    onTrace?.Invoke($"Tavily geslaagd voor {newGame}; {searchHits.Count} bronresultaten, "
                        + $"bruikbare gegevens: {string.Join(", ", missing.Where(fact => GameSearchEvidence.HasFact(searchHits, fact)))}.");
                TraceSearchEvidence(newGame, missing, searchHits);
                if (CanUseOriginalPlan(fullCommand, translatedSource ? alternativeName! : newGame,
                    originalPlan, missing, searchHits))
                {
                    onTrace?.Invoke("Betrouwbare bron leverde alle ontbrekende spelgegevens; bestaand registratieplan wordt direct gecontroleerd.");
                }
                else if (searchHits.Count > 0)
                {
                    plan = await ReplanFromSourcesAsync(fullCommand, plannerCommand, newGame, catalog,
                        today, searchHits, originalPlan, ct);
                    if (translatedSource && plan.Actions is not null)
                        foreach (var action in plan.Actions)
                        {
                            if (action.Kind == "add_boardgame" && (GameTitleMatcher.SameTitle(action.Name, alternativeName)
                                || GameTitleMatcher.SameTitle(action.Name, newGame))) action.Name = newGame;
                            if (action.Kind == "record_session" && (GameTitleMatcher.SameTitle(action.Boardgame, alternativeName)
                                || GameTitleMatcher.SameTitle(action.Boardgame, newGame))) action.Boardgame = newGame;
                        }
                }
                else onTrace?.Invoke("Geen brongegeven naar Gemini doorgestuurd: geen ontbrekend veld met getal en onderwerp in de Tavily-resultaten.");
            }
        }
        if (verifiedGameName is not null && plan.Actions is not null)
            foreach (var action in plan.Actions)
            {
                if (action.Kind == "add_boardgame" && (chosenHit is not null
                    || GameTitleMatcher.SearchScore(action.Name, verifiedGameName).HasValue
                    || GameTitleMatcher.SearchScore(action.Name, verifiedRequestedName).HasValue))
                    action.Name = verifiedGameName;
                if (action.Kind == "record_session" && (chosenHit is not null
                    || GameTitleMatcher.SearchScore(action.Boardgame, verifiedGameName).HasValue
                    || GameTitleMatcher.SearchScore(action.Boardgame, verifiedRequestedName).HasValue))
                    action.Boardgame = verifiedGameName;
            }
        return (plan, searchHits, verifiedGameName, null);
    }

    private static bool CanUseOriginalPlan(string command, string gameName, Plan originalPlan,
        IReadOnlyList<string> missingFacts, IReadOnlyList<GameSearchHit> hits)
    {
        if (hits.Count == 0 || originalPlan.Actions is not { Count: > 0 }
            || !string.IsNullOrWhiteSpace(originalPlan.Question)) return false;

        var singleBggGame = hits.All(hit => TavilyGameSearch.IsMatchingBgg(hit, gameName))
            && hits.Select(TavilyGameSearch.BggId).Distinct().Count() == 1;
        var trustedComplete = hits.Any(hit => GameSearchEvidence.IsTrustedSource(hit)
            && (TavilyGameSearch.IsMatchingBgg(hit, gameName)
                || GamePageSelector.IsMatchingGeneral(hit, gameName))
            && missingFacts.All(fact => GameSearchEvidence.HasFact([hit], fact)));
        if (!singleBggGame && !trustedComplete) return false;
        var nameIsClear = !singleBggGame || !TavilyGameSearch.NeedsNameReview(hits[0], gameName);
        if (!nameIsClear) return false;
        var factsAreComplete = missingFacts.All(fact => GameSearchEvidence.HasFact(hits, fact));
        if (!factsAreComplete) return false;
        var mentionsPlayed = Regex.IsMatch(command, @"\b(?:gespeeld|speelde)\b", RegexOptions.IgnoreCase);
        var hasSessionAction = originalPlan.Actions.Any(action => action.Kind == "record_session");

        // Bij eenduidige spelgegevens uit een betrouwbare bron volstaat het bestaande plan.
        return !mentionsPlayed || hasSessionAction;
    }

    private string? FindNewGameName(string command, string fullCommand, Plan plan, Catalog catalog,
        GameSearchHit? chosenHit, bool rejectedChoices)
    {
        var plannedNames = (plan.Actions ?? []).Select(action => action.Kind switch
        {
            "add_boardgame" => action.Name,
            "record_session" => action.Boardgame,
            _ => null
        });
        var newGame = plannedNames.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)
            && !catalog.Boardgames.Any(game => game.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        if (newGame is null && !string.IsNullOrWhiteSpace(plan.Question) && MissingFactsQuestion.IsMatch(plan.Question))
        {
            var match = GameInQuestion.Match(plan.Question);
            if (match.Success)
            {
                var mentioned = match.Groups["game"].Value.Trim();
                if (!catalog.Boardgames.Any(game => game.Name.Equals(mentioned, StringComparison.OrdinalIgnoreCase)))
                    newGame = mentioned;
            }
        }
        if (rejectedChoices && newGame is null) newGame = PendingSearchName;
        if (chosenHit is not null) newGame = PendingSearchName;
        if (newGame is null && PendingCommand is not null && Regex.IsMatch(command, @"\bzoek\b", RegexOptions.IgnoreCase))
        {
            var match = GameInRegistration.Match(fullCommand);
            if (match.Success && !catalog.Boardgames.Any(game => game.Name.Equals(match.Groups["game"].Value, StringComparison.OrdinalIgnoreCase)))
                newGame = match.Groups["game"].Value;
        }
        return newGame;
    }

    private void TraceSearchEvidence(string gameName, IReadOnlyList<string> missingFacts,
        IReadOnlyList<GameSearchHit> searchHits)
    {
        if (onTrace is null) return;
        try
        {
            foreach (var hit in searchHits)
            {
                var found = GameSearchEvidence.Extract([hit]);
                onTrace($"Tavily-waarden uit {hit.Url}: minimumspelers={Show(found.MinPlayers)}, "
                    + $"maximumspelers={Show(found.MaxPlayers)}, speelduur={Show(found.EstimatedMinutes)} minuten.");
            }
            var combined = GameSearchEvidence.Extract(searchHits);
            onTrace($"Tavily-gecombineerde waarden: minimumspelers={Show(combined.MinPlayers)}, "
                + $"maximumspelers={Show(combined.MaxPlayers)}, speelduur={Show(combined.EstimatedMinutes)} minuten; "
                + $"compleet={combined.Complete}. Een lege waarde betekent geen eenduidige waarde in de bronfragmenten.");
            foreach (var fact in missingFacts)
            {
                var decision = GameSearchEvidence.Resolve(searchHits, fact);
                onTrace($"Broncontrole {gameName}, {fact}: {decision.Reason}; "
                    + (decision.Value is { } value ? $"gekozen {value} via {decision.Sources[0].Url}."
                        : $"waarden {string.Join(", ", decision.Candidates)}."));
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            onTrace($"Tavily-waarden konden niet als getallen worden gelezen: {ex.GetType().Name}.");
        }
        foreach (var fact in missingFacts.Where(fact => !GameSearchEvidence.HasFact(searchHits, fact)))
            onTrace($"Tavily-gegeven ontbreekt: {fact}; geen bronfragment met een getal bij dit onderwerp.");
    }
}

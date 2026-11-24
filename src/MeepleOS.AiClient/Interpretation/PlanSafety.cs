using System.Text.RegularExpressions;
using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.AiClient;

internal sealed class ClarificationNeededException(string question, IReadOnlyList<GameSearchHit>? choices = null)
    : Exception(question)
{
    public IReadOnlyList<GameSearchHit> Choices { get; } = choices ?? [];
}

internal static partial class PlanSafety
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
                        && candidate.Players.Any(player => Names.Equals(CanonicalPlayer(player.Name, catalog.SelfPlayerName), CanonicalPlayer(action.Name, catalog.SelfPlayerName))));
                    if (!isParticipant && !Regex.IsMatch(command,
                            @"\b(?:voeg|registreer|maak)\b[^.;\r\n]*\bspeler\b", RegexOptions.IgnoreCase))
                        throw new ClarificationNeededException($"Wil je {action.Name} als speler toevoegen?");
                    AddPlayer(action.Name, command, players, prepared, catalog.SelfPlayerName, isParticipant);
                    break;

                case "add_boardgame":
                    if (catalog.Boardgames.FirstOrDefault(game => Names.Equals(game.Name, action.Name)) is { IsOwned: false } playedGame
                        && Regex.IsMatch(command, @"\b(?:voeg|toevoegen|zet)\b", RegexOptions.IgnoreCase))
                    {
                        prepared.Add(new ActionPlan { Kind = "add_to_collection", Name = playedGame.Name });
                        break;
                    }
                    AddBoardgame(action.Name, command, games, prepared, searchHits, onTrace, verifiedGameName, action);
                    break;

                case "add_to_collection":
                    if (!Regex.IsMatch(command, @"\b(?:voeg|toevoegen|zet)\b", RegexOptions.IgnoreCase))
                        throw new ClarificationNeededException("Wil je dit bordspel aan je collectie toevoegen?");
                    prepared.Add(action);
                    break;

                case "add_expansion":
                    AddExpansion(action.Name, action.Boardgame, command, games, expansions, prepared);
                    break;

                case "record_session":
                    PrepareSession(action, command, games, players, expansions, prepared, searchHits, onTrace, verifiedGameName, catalog.SelfPlayerName);
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

}

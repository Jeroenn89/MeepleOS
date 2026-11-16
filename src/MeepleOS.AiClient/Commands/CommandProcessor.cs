using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

public sealed partial class CommandProcessor(IMeepleTools tools, IPlanner planner, bool readOnly = false,
    string? pendingCommand = null, IGameSearch? gameSearch = null, Action<Exception>? onError = null,
    Action<string>? onTrace = null, string? pendingQuestion = null,
    IReadOnlyList<GameSearchHit>? pendingChoices = null, string? pendingSearchName = null,
    IReadOnlyCollection<int>? rejectedBggIds = null)
{
    private static readonly Regex NewRequest = new(
        @"^\s*(?:ik\s+(?:speelde\b|heb\b[^\r\n.!?;]*\bgespeeld\b|wil\b[^\r\n.!?;]*\btoevoegen\b)|(?:toon|geef|welke|wat|wie|hoeveel|staat|vertel|registreer|voeg|verwijder|wijzig|hernoem)\b)|\b(?:registreren|registreer|gespeeld|speelde)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FollowUpWithoutQuestion = new(
        @"^\s*(?:probeer|proberen|opnieuw|herhaal|ga\s+verder|verder|zoek\s+(?:het\s+)?op)\b|^\s*\d+\s*(?:minuten|minutes?|min\.?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BoardgameQuestion = new(
        @"\b(?:welk|welke|wat)\b[^?\r\n]*\b(?:bordspel|spel|editie)\b|\b(?:bordspel|spel|editie)\b[^?\r\n]*\bbedoel\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MissingFactsQuestion = new(@"\b(?:speelduur|spelersaantal|aantal spelers|minimumaantal spelers|maximumaantal spelers|speeltijd)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GameInQuestion = new(@"\b(?:voor|van)\s+(?<game>.+?)\s+(?:ontbreekt|ontbreken|in minuten|heeft|is)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GameInRegistration = new(@"\b(?:registreer|registreren|bordspel)\s+(?<game>[\p{L}\p{N}][\p{L}\p{N}'-]*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RegistrationIntent = new(@"\b(?:registreer|registreren|voeg\s+(?:het\s+)?bordspel)\b|\bik\s+(?:speelde|heb\b[^.;\r\n]*\bgespeeld)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string? PendingCommand { get; private set; } = pendingCommand;
    public string? PendingQuestion { get; private set; } = pendingQuestion;
    public IReadOnlyList<GameSearchHit> PendingChoices { get; private set; } = pendingChoices ?? [];
    public string? PendingSearchName { get; private set; } = pendingSearchName;
    public IReadOnlyCollection<int> RejectedBggIds { get; private set; } = rejectedBggIds ?? [];
    private static readonly HashSet<string> ReadKinds = ["get_boardgames", "get_players", "get_expansions", "get_sessions", "get_player_statistics", "get_boardgame_statistics", "get_leaderboard", "get_play_statistics"];
    private static readonly HashSet<string> Kinds = [.. ReadKinds, "add_player", "add_boardgame", "add_expansion", "record_session", "update_player", "update_boardgame", "update_expansion", "update_session", "delete_player", "delete_boardgame", "delete_expansion", "delete_session"];

    public async Task<CommandResult> RunAsync(string command, DateOnly today, CancellationToken ct = default)
    {
        var immediateResult = PrepareConversation(command);
        if (immediateResult is not null) return immediateResult;

        var fullCommand = PendingCommand is null ? command : $"{PendingCommand}{Environment.NewLine}Aanvullende gegevens: {command}";
        onTrace?.Invoke($"Pending={(PendingCommand is not null)}; contextlengte={fullCommand.Length}.");
        var countBefore = (planner as IRequestCounter)?.RequestCount ?? 0;
        var completed = new List<string>();
        try
        {
            var catalog = await ReadCatalog(ct);
            onTrace?.Invoke($"Catalogus: {catalog.Boardgames.Count} spellen, {catalog.Players.Count} spelers, {catalog.Expansions.Count} uitbreidingen.");
            var interpretation = await InterpretAsync(command, fullCommand, catalog, today, ct);
            var research = await ResearchGameAsync(command, fullCommand, interpretation.PlannerCommand,
                interpretation.Plan, catalog, interpretation.ChosenHit, interpretation.RejectedChoices,
                today, countBefore, ct);
            if (research.EarlyResult is not null) return research.EarlyResult;

            return await FinishAsync(fullCommand, research.Plan, catalog, research.Hits,
                research.VerifiedGameName, countBefore, completed, ct);
        }
        catch (ClarificationNeededException ex)
        {
            onTrace?.Invoke($"Spelgegevens of andere registratiegegevens afgewezen: {ex.Message}");
            PendingCommand = fullCommand;
            PendingQuestion = ex.Message;
            PendingChoices = ex.Choices;
            var count = (planner as IRequestCounter)?.RequestCount - countBefore ?? 1;
            return new CommandResult(ex.Message, count);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            if (ex is GameSearchUnavailableException) onTrace?.Invoke($"Tavily-aanroep mislukt: {ex.Message}");
            if (RegistrationIntent.IsMatch(fullCommand) && (completed.Count == 0 || fullCommand.Contains("gespeeld", StringComparison.OrdinalIgnoreCase)))
                PendingCommand = fullCommand;
            else if (completed.Count == 0)
            {
                PendingCommand = null;
                PendingQuestion = null;
                PendingChoices = [];
                PendingSearchName = null;
                RejectedBggIds = [];
            }
            var count = (planner as IRequestCounter)?.RequestCount - countBefore ?? 1;
            var prefix = completed.Count == 0 ? "" : $"Wel opgeslagen: {string.Join("; ", completed)}{Environment.NewLine}Niet voltooid: ";
            var message = ex.Message.StartsWith("MCP ", StringComparison.Ordinal) ? "De MCP-server kon de opdracht niet afronden. Probeer het opnieuw." : ex.Message;
            return new CommandResult(prefix + message, count);
        }
    }

    private CommandResult? PrepareConversation(string command)
    {
        if (PendingCommand is not null && command.Trim().Equals("annuleer", StringComparison.OrdinalIgnoreCase))
        {
            PendingCommand = null;
            PendingQuestion = null;
            PendingChoices = [];
            PendingSearchName = null;
            RejectedBggIds = [];
            return new CommandResult("Opdracht geannuleerd; niets opgeslagen.", 0);
        }

        if (PendingCommand is not null && (NewRequest.IsMatch(command)
            || PendingQuestion is null && !FollowUpWithoutQuestion.IsMatch(command)))
        {
            onTrace?.Invoke("Nieuwe opdracht vervangt de lopende registratie.");
            PendingCommand = null;
            PendingQuestion = null;
            PendingChoices = [];
            PendingSearchName = null;
            RejectedBggIds = [];
        }
        return null;
    }
}

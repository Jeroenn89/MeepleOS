using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

public sealed class CommandProcessor(IMeepleTools tools, IPlanner planner, bool readOnly = false, string? pendingCommand = null)
{
    private static readonly Regex NewRequest = new(
        @"^\s*(?:ik\s+(?:speelde\b|heb\b[^\r\n.!?;]*\bgespeeld\b)|(?:toon|geef|welke|wat|wie|hoeveel|registreer|voeg|verwijder|wijzig|hernoem)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BoardgameQuestion = new(
        @"\b(?:welk|welke|wat)\b[^?\r\n]*\b(?:bordspel|spel)\b|\b(?:bordspel|spel)\b[^?\r\n]*\bbedoel\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string? PendingCommand { get; private set; } = pendingCommand;
    private static readonly HashSet<string> ReadKinds = ["get_boardgames", "get_players", "get_expansions", "get_sessions", "get_player_statistics", "get_boardgame_statistics", "get_leaderboard", "get_play_statistics"];
    private static readonly HashSet<string> Kinds = [.. ReadKinds, "add_player", "add_boardgame", "add_expansion", "record_session", "update_player", "update_boardgame", "update_expansion", "update_session", "delete_player", "delete_boardgame", "delete_expansion", "delete_session"];

    public async Task<CommandResult> RunAsync(string command, DateOnly today, CancellationToken ct = default)
    {
        if (PendingCommand is not null && command.Trim().Equals("annuleer", StringComparison.OrdinalIgnoreCase))
        {
            PendingCommand = null;
            return new CommandResult("Opdracht geannuleerd; niets opgeslagen.", 0);
        }

        if (PendingCommand is not null && NewRequest.IsMatch(command))
            PendingCommand = null;

        var fullCommand = PendingCommand is null ? command : $"{PendingCommand}{Environment.NewLine}Aanvullende gegevens: {command}";
        var countBefore = (planner as IRequestCounter)?.RequestCount ?? 0;
        var completed = new List<string>();
        try
        {
            var catalog = await ReadCatalog(ct);
            var knownGame = BoardgameMention.FindUnique(catalog, fullCommand);
            var plannerCommand = knownGame is null ? fullCommand
                : $"{BoardgameMention.CanonicalizeCommand(fullCommand, knownGame)}{Environment.NewLine}Bestaand bordspel uit de catalogus: {knownGame.Name}. Gebruik exact deze naam en vraag niet welk spel bedoeld is.";
            var plan = await planner.PlanAsync(plannerCommand, catalog, today, ct);
            if (knownGame is not null && !string.IsNullOrWhiteSpace(plan.Question)
                && BoardgameQuestion.IsMatch(plan.Question))
            {
                plan = await planner.PlanAsync(
                    $"{plannerCommand}{Environment.NewLine}Verduidelijking: het bedoelde bordspel is {knownGame.Name}. Maak nu het volledige plan voor de oorspronkelijke opdracht.",
                    catalog, today, ct);
            }
            if (knownGame is not null && plan.Actions is not null)
                BoardgameMention.ResolvePlanReferences(plan, knownGame);
            var count = (planner as IRequestCounter)?.RequestCount - countBefore ?? 1;
            if (!string.IsNullOrWhiteSpace(plan.Question))
            {
                PendingCommand = fullCommand;
                return new CommandResult(plan.Question!, count);
            }

            plan = PlanSafety.Prepare(plan, catalog, fullCommand, readOnly);
            Validate(plan, catalog, fullCommand, readOnly);
            PendingCommand = null;
            foreach (var action in plan.Actions)
            {
                var message = await Execute(action, catalog, ct);
                completed.Add(message);
            }
            return new CommandResult(completed.Count == 0 ? "Geen actie nodig." : string.Join(Environment.NewLine, completed), count);
        }
        catch (ClarificationNeededException ex)
        {
            PendingCommand = fullCommand;
            var count = (planner as IRequestCounter)?.RequestCount - countBefore ?? 1;
            return new CommandResult(ex.Message, count);
        }
        catch (Exception ex)
        {
            var count = (planner as IRequestCounter)?.RequestCount - countBefore ?? 1;
            var prefix = completed.Count == 0 ? "" : $"Wel opgeslagen: {string.Join("; ", completed)}{Environment.NewLine}Niet voltooid: ";
            return new CommandResult(prefix + ex.Message, count);
        }
    }

    private async Task<Catalog> ReadCatalog(CancellationToken ct)
    {
        var games = await tools.CallAsync("get_boardgames", ct: ct);
        var players = await tools.CallAsync("get_players", ct: ct);
        var expansions = await tools.CallAsync("get_game_expansions", ct: ct);
        static List<Item> Read(JsonElement array) => array.EnumerateArray().Select(x => new Item(
            x.GetProperty("id").GetInt32(), x.GetProperty("name").GetString()!,
            x.TryGetProperty("boardGameId", out var id) ? id.GetInt32() : null)).ToList();
        return new Catalog(Read(games), Read(players), Read(expansions));
    }

    private static Item? Find(List<Item> list, string? name) => list.FirstOrDefault(x =>
        string.Equals(x.Name.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static void Validate(Plan plan, Catalog catalog, string command, bool readOnly)
    {
        if (plan.Actions is null || plan.Actions.Count > 20) throw new InvalidOperationException("Ongeldig uitvoeringsplan.");
        var games = catalog.Boardgames.ToList(); var players = catalog.Players.ToList(); var expansions = catalog.Expansions.ToList();
        foreach (var a in plan.Actions)
        {
            if (!Kinds.Contains(a.Kind)) throw new InvalidOperationException($"Onbekende actie: {a.Kind}.");
            if (readOnly && !ReadKinds.Contains(a.Kind)) throw new InvalidOperationException("Alleen-lezenstand: toevoegen, wijzigen, verwijderen en potjes registreren zijn geblokkeerd.");
            if (a.Kind.StartsWith("delete_") && !command.Contains("verwijder", StringComparison.OrdinalIgnoreCase) && !command.Contains("wis", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Verwijderen vereist een expliciete opdracht.");
            if (a.Kind.StartsWith("update_") && !command.Contains("wijzig", StringComparison.OrdinalIgnoreCase) && !command.Contains("hernoem", StringComparison.OrdinalIgnoreCase) && !command.Contains("pas", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Wijzigen vereist een expliciete opdracht.");
            switch (a.Kind)
            {
                case "add_player":
                    RequireName(a.Name); if (Find(players, a.Name) is null) players.Add(new Item(0, a.Name!.Trim())); break;
                case "add_boardgame":
                    RequireName(a.Name);
                    if (Find(games, a.Name) is null)
                    {
                        if (a.MinPlayers is null or < 1) throw new InvalidOperationException($"Wat is het minimumaantal spelers voor {a.Name}?");
                        if (!a.NoMaximum && a.MaxPlayers is null) throw new InvalidOperationException($"Wat is het maximumaantal spelers voor {a.Name} (of is er geen vast maximum)?");
                        if (a.MaxPlayers.HasValue && a.MaxPlayers < a.MinPlayers) throw new InvalidOperationException($"Wat is het juiste maximumaantal spelers voor {a.Name}?");
                        if (a.EstimatedMinutes is null or < 1) throw new InvalidOperationException($"Wat is de geschatte speelduur van {a.Name} in minuten?");
                        games.Add(new Item(0, a.Name!.Trim()));
                    }
                    break;
                case "add_expansion":
                    RequireName(a.Name); RequireGame(games, a.Boardgame);
                    if (!expansions.Any(x => x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase) && x.BoardGameId == Find(games, a.Boardgame)!.Id)) expansions.Add(new Item(0, a.Name!.Trim(), Find(games, a.Boardgame)!.Id));
                    break;
                case "record_session":
                    RequireGame(games, a.Boardgame);
                    if (!DateOnly.TryParseExact(a.PlayedAt, "yyyy-MM-dd", out _)) throw new InvalidOperationException("Geef de speeldatum op (YYYY-MM-DD).");
                    if (a.Players.Count == 0 || a.Winners.Count == 0) throw new InvalidOperationException("Voor een potje zijn deelnemers en winnaar(s) nodig.");
                    var names = a.Players.Select(p => p.Name.Trim()).ToList();
                    if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) throw new InvalidOperationException("Een deelnemer staat dubbel in het plan.");
                    foreach (var p in a.Players) if (Find(players, p.Name) is null) throw new InvalidOperationException($"Speler {p.Name} is onbekend.");
                    foreach (var w in a.Winners) if (!names.Contains(w.Trim(), StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"Winnaar {w} is geen deelnemer.");
                    if (a.Winners.Distinct(StringComparer.OrdinalIgnoreCase).Count() != a.Winners.Count) throw new InvalidOperationException("Een winnaar staat dubbel in het plan.");
                    foreach (var e in a.Expansions) if (!expansions.Any(x => x.Name.Equals(e, StringComparison.OrdinalIgnoreCase) && x.BoardGameId == Find(games, a.Boardgame)!.Id)) throw new InvalidOperationException($"Uitbreiding {e} hoort niet bij {a.Boardgame}.");
                    break;
                case "update_player": case "delete_player": RequireId(players, a.Id); break;
                case "update_boardgame": case "delete_boardgame": RequireId(games, a.Id); break;
                case "update_expansion": case "delete_expansion": RequireId(expansions, a.Id); break;
                case "update_session": case "delete_session": if (a.Id is null or < 1) throw new InvalidOperationException("Een bestaand potje-ID is nodig."); break;
            }
            if (a.Kind.StartsWith("update_") && a.Kind != "update_player")
            {
                if (string.IsNullOrWhiteSpace(a.ChangesJson)) throw new InvalidOperationException("Wijzigingsvelden ontbreken.");
                using var changes = JsonDocument.Parse(a.ChangesJson);
                if (changes.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Wijzigingen moeten een JSON-object zijn.");
            }
            if (a.Kind == "update_player") RequireName(a.Name);
        }
    }

    private static void RequireName(string? name)
    { if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new InvalidOperationException("Een geldige naam ontbreekt."); }
    private static void RequireGame(List<Item> games, string? name)
    { if (Find(games, name) is null) throw new InvalidOperationException($"Bordspel {name ?? "(onbekend)"} is onbekend."); }
    private static void RequireId(List<Item> items, int? id)
    { if (id is null or < 1 || !items.Any(x => x.Id == id)) throw new InvalidOperationException($"ID {id} bestaat niet in de huidige catalogus."); }

    private async Task<string> Execute(ActionPlan a, Catalog catalog, CancellationToken ct)
    {
        JsonElement result;
        switch (a.Kind)
        {
            case "get_boardgames": return FormatItems("Bordspellen", catalog.Boardgames);
            case "get_players": return FormatItems("Spelers", catalog.Players);
            case "get_expansions":
                return FormatItems("Uitbreidingen", catalog.Expansions, x => $"{x.Name} ({catalog.Boardgames.FirstOrDefault(g => g.Id == x.BoardGameId)?.Name ?? "onbekend bordspel"})");
            case "add_player":
                if (Find(catalog.Players, a.Name) is { } existingPlayer) return $"Speler {existingPlayer.Name} bestaat al.";
                result = await tools.CallAsync("add_player", new { name = a.Name!.Trim() }, ct);
                catalog.Players.Add(new Item(result.GetProperty("id").GetInt32(), result.GetProperty("name").GetString()!));
                return $"Speler {a.Name} toegevoegd.";
            case "add_boardgame":
                if (Find(catalog.Boardgames, a.Name) is { } existingGame) return $"Bordspel {existingGame.Name} bestaat al.";
                result = await tools.CallAsync("add_boardgame", new { name = a.Name!.Trim(), minPlayers = a.MinPlayers, maxPlayers = a.NoMaximum ? null : a.MaxPlayers, estimatedMinutes = a.EstimatedMinutes }, ct);
                catalog.Boardgames.Add(new Item(result.GetProperty("id").GetInt32(), result.GetProperty("name").GetString()!));
                return $"Bordspel {a.Name} toegevoegd.";
            case "add_expansion":
                var game = Find(catalog.Boardgames, a.Boardgame)!;
                if (catalog.Expansions.Any(x => x.BoardGameId == game.Id && x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase))) return $"Uitbreiding {a.Name} bestaat al.";
                result = await tools.CallAsync("add_game_expansion", new { boardGameId = game.Id, name = a.Name!.Trim(), description = a.Description }, ct);
                catalog.Expansions.Add(new Item(result.GetProperty("id").GetInt32(), result.GetProperty("name").GetString()!, game.Id));
                return $"Uitbreiding {a.Name} aan {game.Name} toegevoegd.";
            case "record_session":
                var boardgame = Find(catalog.Boardgames, a.Boardgame)!;
                var date = DateOnly.ParseExact(a.PlayedAt!, "yyyy-MM-dd");
                var participants = a.Players.Select(p => new { playerId = Find(catalog.Players, p.Name)!.Id, score = p.Score, position = p.Position }).ToList();
                var winners = a.Winners.Select(w => Find(catalog.Players, w)!.Id).ToList();
                var expansionIds = a.Expansions.Select(e => catalog.Expansions.Single(x => x.BoardGameId == boardgame.Id && x.Name.Equals(e, StringComparison.OrdinalIgnoreCase)).Id).ToList();
                var sessions = await tools.CallAsync("get_game_sessions", new { boardGameId = boardgame.Id, from = a.PlayedAt, to = a.PlayedAt }, ct);
                foreach (var session in sessions.EnumerateArray())
                {
                    var existingIds = session.GetProperty("players").EnumerateArray().Select(x => x.GetProperty("playerId").GetInt32()).Order().ToArray();
                    if (existingIds.SequenceEqual(participants.Select(x => x.playerId).Order()))
                        return $"Potje {boardgame.Name} op {a.PlayedAt} met deze deelnemers bestaat al; niets dubbel geregistreerd.";
                }
                await tools.CallAsync("record_game_session", new { boardGameId = boardgame.Id, playedAt = a.PlayedAt, players = participants, winnerPlayerIds = winners, expansionIds, notes = a.Notes }, ct);
                return $"Potje {boardgame.Name} geregistreerd: {string.Join(", ", a.Players.Select(p => p.Name))}. Winnaar(s): {string.Join(", ", a.Winners)}.";
            case "get_sessions": return (await tools.CallAsync("get_game_sessions", ct: ct)).ToString();
            case "get_player_statistics": case "get_boardgame_statistics": case "get_leaderboard": case "get_play_statistics":
                return (await tools.CallAsync(a.Kind, ct: ct)).ToString();
            default:
                var tool = a.Kind.Replace("_expansion", "_game_expansion").Replace("_session", "_game_session");
                object args = a.Kind == "update_player" ? new { id = a.Id, name = a.Name } : a.Kind.StartsWith("update_") ? new { id = a.Id, changes = JsonDocument.Parse(a.ChangesJson!).RootElement } : new { id = a.Id };
                result = await tools.CallAsync(tool, args, ct);
                if (a.Kind.StartsWith("delete_") && result.ValueKind == JsonValueKind.False) throw new InvalidOperationException($"{tool} bevestigde de verwijdering niet.");
                return $"{tool} voor ID {a.Id} uitgevoerd.";
        }
    }

    private static string FormatItems(string title, List<Item> items, Func<Item, string>? label = null) =>
        items.Count == 0 ? $"{title}: geen." : $"{title}: {string.Join(", ", items.Select(label ?? (x => x.Name)))}.";
}

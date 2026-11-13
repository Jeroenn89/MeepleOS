using System.Text.Json;
using MeepleOS.AiClient;
using MeepleOS.BusinessLayer.Services;

if (args.Contains("--source-tests", StringComparer.OrdinalIgnoreCase))
{
    await RunSourceTests(includeMcp: true);
    return;
}
if (args.Contains("--source-unit-tests", StringComparer.OrdinalIgnoreCase))
{
    await RunSourceTests(includeMcp: false);
    return;
}

var passed = 0;
{
    var planner = new SequencePlanner(
        new Plan { Question = "Wie won en wanneer?" },
        new Plan { Question = "Wie won Monopoly?" },
        new Plan { Question = "Wie won Freakytown?" },
        new Plan { Actions = [Session("De Zoektocht naar El Dorado", ["Jeroen", "Wim"], ["Wim"])] });
    var processor = new CommandProcessor(new FakeTools(), planner);
    var date = new DateOnly(2026, 11, 12);
    await processor.RunAsync("Ik heb El Dorado gespeeld met Wim.", date);
    await processor.RunAsync("Ik wil Monopoly registreren met Pallas.", date);
    await processor.RunAsync("Kun je Freakytown registreren met Roan?", date);
    if (planner.Commands[1].Contains("El Dorado") || planner.Commands[2].Contains("Monopoly")
        || planner.Commands[2].Contains("El Dorado") || !planner.Commands[2].Contains("Freakytown"))
        throw new Exception("Een nieuwe registratie kreeg oude AI-context.");
    await processor.RunAsync("vandaag", date);
    if (!planner.Commands[3].Contains("Freakytown") || !planner.Commands[3].Contains("Aanvullende gegevens: vandaag"))
        throw new Exception("Aanvullende informatie verloor de lopende registratie.");
    passed++;
}
await Check("Automatische winnaar uit volgordescores", new Plan { Actions = [Session("De Zoektocht naar El Dorado", ["Jeroen", "Wim", "Nick"], [])] },
    t => t.LastRecord?.GetProperty("players")[0].GetProperty("score").GetInt32() == 30
        && t.LastRecord?.GetProperty("players")[1].GetProperty("score").GetInt32() == 40
        && t.LastRecord?.GetProperty("players")[2].GetProperty("score").GetInt32() == 50
        && t.LastRecord?.GetProperty("winnerPlayerIds")[0].GetInt32() == 12,
    command: "Ik heb vandaag El Dorado gespeeld met Wim en Nick. De scores waren in die volgorde 30, 40 en 50.");
{
    var search = new FakeGameSearch();
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, new SequencePlanner(new Plan { Actions = [Session("El Dorado", ["Jeroen", "Wim"], ["Wim"])] }), gameSearch: search)
        .RunAsync("Ik speelde El Dorado met Wim; Wim won.", new DateOnly(2026, 11, 11));
    if (!result.Message.Contains("De Zoektocht naar El Dorado geregistreerd") || search.Calls != 0)
        throw new Exception("El Dorado werd niet als bestaand spel herkend.");
    passed++;
}
await Check("Gedeelde overwinning", new Plan { Actions = [new ActionPlan { Kind = "record_session", Boardgame = "Carcassonne", PlayedAt = "2026-11-11", Players = [new() { Name = "Jeroen", Score = 20 }, new() { Name = "Pallas", Score = 40 }, new() { Name = "Roan", Score = 40 }] }] },
    t => t.LastRecord?.GetProperty("winnerPlayerIds").GetArrayLength() == 2);
await Check("Expliciete winnaar heeft voorrang", new Plan { Actions = [new ActionPlan { Kind = "record_session", Boardgame = "Carcassonne", PlayedAt = "2026-11-11", Players = [new() { Name = "Jeroen", Score = 20 }, new() { Name = "Pallas", Score = 40 }], Winners = ["Jeroen"] }] },
    t => t.LastRecord?.GetProperty("winnerPlayerIds")[0].GetInt32() == 2);
{
    var processor = new CommandProcessor(new FakeTools(), new FakePlanner(new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Pallas"], [])] }, false));
    var missing = await processor.RunAsync("Ik speelde Carcassonne met Pallas.", new DateOnly(2026, 11, 11));
    if (!missing.Message.Contains("Wie heeft gewonnen") || processor.PendingCommand is null)
        throw new Exception("Ontbrekende winnaar werd niet gevraagd.");
    passed++;
}
if (!SessionWinnerResolver.Resolve([new(1, 5), new(2, 1)], null, ScoreWinRule.Lowest).SequenceEqual([2]))
    throw new Exception("Laagste score winregel faalde.");
passed++;
{
    var planner = new SequencePlanner(new Plan { Question = "Wie won en wanneer speelde je?" },
        new Plan { Actions = [Session("De Zoektocht naar El Dorado", ["Jeroen", "Wim", "Nick"], ["Wim"])] });
    var tools = new FakeTools();
    var processor = new CommandProcessor(tools, planner);
    var first = await processor.RunAsync("Ik heb El Dorado gespeeld met Wim en Nick.", new DateOnly(2026, 11, 12));
    var second = await processor.RunAsync("Vandaag, Wim heeft gewonnen.", new DateOnly(2026, 11, 12));
    if (first.GeminiRequests != 1 || !planner.Commands[1].Contains("El Dorado gespeeld")
        || !second.Message.Contains("geregistreerd") || processor.PendingCommand is not null)
        throw new Exception("Gesprekscontext ging verloren.");
    if (new CommandProcessor(new FakeTools(), new SequencePlanner(new Plan { Actions = [new() { Kind = "get_players" }] }))
            .PendingCommand is not null)
        throw new Exception("Een nieuwe chatsessie erfde context.");
    passed++;
}
await Check("Bestaande speler en spel", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Pallas"], ["Jeroen"])] }, t => t.Calls.Contains("record_game_session"));
await Check("Typefout in bestaand spel en onbekende deelnemer", new Plan { Actions = [Session("munckin", ["Roan", "Isaac", "Jeroen"], ["Isaac"])] },
    t => t.Calls.IndexOf("add_player") < t.Calls.IndexOf("record_game_session")
        && !t.Calls.Contains("add_boardgame")
        && t.LastRecord?.GetProperty("boardGameId").GetInt32() == 8
        && t.LastRecord?.GetProperty("players").GetArrayLength() == 3
        && t.LastRecord?.GetProperty("winnerPlayerIds")[0].GetInt32() == 4,
    command: "hallo ik heb een snel potje munckin gespeeld met roan en isaac en jeroen vandaag en isaac won");
await Check("Ontbrekende spelnaam in plan wordt hersteld", new Plan { Actions = [Session("", ["Roan", "Isaac", "Jeroen"], ["Isaac"])] },
    t => t.LastRecord?.GetProperty("boardGameId").GetInt32() == 8 && !t.Calls.Contains("add_boardgame"),
    command: "Ik heb MUNCKIN gespeeld met Roan, Isaac en Jeroen; Isaac won.");
await Check("Gemini wil bestaand spel dubbel toevoegen", new Plan { Actions = [new() { Kind = "add_boardgame", Name = "munckin" }, Session("munckin", ["Jeroen", "Pallas"], ["Jeroen"])] },
    t => !t.Calls.Contains("add_boardgame") && t.LastRecord?.GetProperty("boardGameId").GetInt32() == 8,
    command: "Ik heb munckin gespeeld met Jeroen en Pallas; Jeroen won.");
{
    var tools = new FakeTools();
    var planner = new SequencePlanner(
        new Plan { Question = "Welk bordspel bedoel je?" },
        new Plan { Actions = [Session("", ["Roan", "Isaac", "Jeroen"], ["Isaac"])] });
    var processor = new CommandProcessor(tools, planner);
    var result = await processor.RunAsync("hallo ik heb een snel potje munckin gespeeld met roan en isaac en jeroen vandaag en isaac won", new DateOnly(2026, 11, 12));
    if (planner.Commands.Count != 2 || !planner.Commands[0].Contains("Munchkin gespeeld")
        || planner.Commands[0].Contains("munckin gespeeld")
        || !planner.Commands[1].Contains("Munchkin")
        || result.GeminiRequests != 2 || !result.Message.Contains("geregistreerd")
        || processor.PendingCommand is not null || tools.LastRecord?.GetProperty("boardGameId").GetInt32() != 8)
        throw new Exception($"Gemini-vraag over bekend spel is niet afgehandeld: {result.Message}");
    passed++;
}
{
    var original = "Ik heb munckin gespeeld met Roan, Isaac en Jeroen; Isaac won.";
    var tools = new FakeTools();
    var planner = new SequencePlanner(new Plan { Actions = [Session("", ["Roan", "Isaac", "Jeroen"], ["Isaac"])] });
    var processor = new CommandProcessor(tools, planner, pendingCommand: original);
    var result = await processor.RunAsync("MUNCKIN!!!", new DateOnly(2026, 11, 12));
    if (!planner.Commands[0].Contains("Munchkin gespeeld") || !result.Message.Contains("geregistreerd")
        || tools.LastRecord?.GetProperty("boardGameId").GetInt32() != 8)
        throw new Exception($"Een verduidelijking van de spelnaam werkte niet: {result.Message}");
    passed++;
}
await Check("Onbekende speler", new Plan { Actions = [new() { Kind = "add_player", Name = "Kevin" }, Session("Carcassonne", ["Jeroen", "Kevin"], ["Kevin"])] },
    t => t.Calls.SequenceEqual(["get_boardgames", "get_players", "get_game_expansions", "add_player", "get_game_sessions", "record_game_session"]),
    command: "Ik speelde Carcassonne met Jeroen en Kevin; Kevin won.");
await Check("Automatisch nieuwe deelnemer", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Olivier"], ["Jeroen"])] },
    t => t.Calls.SequenceEqual(["get_boardgames", "get_players", "get_game_expansions", "add_player", "get_game_sessions", "record_game_session"])
        && t.LastRecord?.GetProperty("players")[1].GetProperty("playerId").GetInt32() == 4,
    command: "Ik speelde Carcassonne met Jeroen en Olivier; Jeroen won.");
await Check("Bestaande speler niet dubbel toevoegen", new Plan { Actions = [new() { Kind = "add_player", Name = "Pallas" }, Session("Carcassonne", ["Jeroen", "Pallas"], ["Pallas"])] },
    t => !t.Calls.Contains("add_player") && t.LastRecord?.GetProperty("players")[1].GetProperty("playerId").GetInt32() == 3,
    command: "Ik speelde Carcassonne met Jeroen en Pallas; Pallas won.");
await Check("Ik en Jeroen tellen eenmaal", new Plan { Actions = [Session("Carcassonne", ["Ik", "Jeroen", "Pallas"], ["Ik", "Jeroen"])] },
    t => t.LastRecord?.GetProperty("players").GetArrayLength() == 2
        && t.LastRecord?.GetProperty("winnerPlayerIds").GetArrayLength() == 1,
    command: "Ik speelde Carcassonne met Jeroen en Pallas; ik won.");
await Check("Jeroen Pallas zijn twee bestaande spelers", new Plan { Actions = [new() { Kind = "add_player", Name = "Jeroen Pallas" }, Session("Carcassonne", ["Jeroen Pallas"], ["Jeroen"])] },
    t => !t.Calls.Contains("add_player") && t.LastRecord?.GetProperty("players").GetArrayLength() == 2,
    command: "Ik speelde Carcassonne met Jeroen Pallas; Jeroen won.");
await Check("Nieuw bordspel", new Plan { Actions = [new() { Kind = "add_boardgame", Name = "Nieuw", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 45 }] },
    t => t.Calls.Contains("add_boardgame"), command: "Voeg Nieuw toe voor 2 tot 4 spelers, 45 minuten.");
await Check("Nieuw bordspel zonder vast maximum", new Plan { Actions = [new() { Kind = "add_boardgame", Name = "Vrij Spel" }] },
    t => t.Calls.Contains("add_boardgame"),
    command: "Voeg Vrij Spel toe met minimaal 2 spelers, geen vast maximum en 30 minuten speelduur.");
await Check("Verplichte informatie ontbreekt", new Plan { Actions = [new() { Kind = "add_boardgame", Name = "Nieuw" }] }, t => !t.Calls.Contains("add_boardgame"));
await Check("Onbekend spel zonder specificaties", new Plan { Actions = [Session("FreakyTown", ["Jeroen", "Pallas"], ["Jeroen"])] },
    t => !t.Calls.Any(call => call.StartsWith("add_") || call == "record_game_session"),
    command: "Ik speelde FreakyTown met Jeroen en Pallas; Jeroen won.");
await Check("Verzonnen spelgegevens worden geweigerd", new Plan { Actions = [new() { Kind = "add_boardgame", Name = "FreakyTown", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 30 }, Session("FreakyTown", ["Jeroen", "Pallas"], ["Jeroen"])] },
    t => !t.Calls.Contains("add_boardgame") && !t.Calls.Contains("record_game_session"),
    command: "Ik speelde FreakyTown met Jeroen en Pallas; Jeroen won.");
await Check("Onbekende uitbreiding", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Pallas"], ["Jeroen"], ["Nieuwe Rivier"])] },
    t => t.Calls.IndexOf("add_game_expansion") < t.Calls.IndexOf("record_game_session")
        && t.LastRecord?.GetProperty("expansionIds")[0].GetInt32() == 10,
    command: "Ik speelde Carcassonne met Jeroen en Pallas en uitbreiding Nieuwe Rivier; Jeroen won.");
await Check("Onzekere deelnemer", new Plan { Actions = [Session("Carcassonne", ["Jeroen Olivier", "Pallas"], ["Pallas"])] },
    t => !t.Calls.Contains("add_player") && !t.Calls.Contains("record_game_session"),
    command: "Ik speelde Carcassonne met Jeroen en Olivier en Pallas; Pallas won.");
await Check("Toeschouwer wordt geen speler", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Olivier"], ["Jeroen"])] },
    t => !t.Calls.Contains("add_player") && !t.Calls.Contains("record_game_session"),
    command: "Ik speelde Carcassonne met Jeroen; Olivier keek toe; Jeroen won.");
await Check("Losse toeschouwer wordt niet toegevoegd", new Plan { Actions = [new() { Kind = "add_player", Name = "Olivier" }, Session("Carcassonne", ["Jeroen", "Pallas"], ["Jeroen"])] },
    t => !t.Calls.Contains("add_player") && !t.Calls.Contains("record_game_session"),
    command: "Ik speelde Carcassonne met Jeroen en Pallas; Olivier keek toe; Jeroen won.");
await Check("Ongeldig plan schrijft ook geen nieuwe speler", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Olivier"], ["Pallas"])] },
    t => !t.Calls.Contains("add_player") && !t.Calls.Contains("record_game_session"),
    command: "Ik speelde Carcassonne met Jeroen en Olivier; Pallas won.");
await Check("Optionele score", new Plan { Actions = [new ActionPlan { Kind = "record_session", Boardgame = "Carcassonne", PlayedAt = "2026-11-11", Players = [new() { Name = "Jeroen", Score = 50 }, new() { Name = "Pallas", Score = 30 }], Winners = ["Jeroen"] }] }, t => t.LastRecord?.GetProperty("players")[0].GetProperty("score").GetInt32() == 50);
await Check("Uitbreiding", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Pallas"], ["Pallas"], ["De Rivier"])] }, t => t.LastRecord?.GetProperty("expansionIds")[0].GetInt32() == 7);
await Check("Onduidelijke naam", new Plan { Question = "Welke Kevin bedoel je?" }, t => !t.Calls.Contains("record_game_session"));
await Check("MCP-fout", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Kevin"], ["Kevin"])] },
    t => { t.FailRecord = true; return true; }, true,
    command: "Ik speelde Carcassonne met Jeroen en Kevin; Kevin won.");
await Check("Bestaand potje niet dubbel registreren", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Pallas"], ["Jeroen"])] },
    t => t.Calls.Contains("get_game_sessions") && !t.Calls.Contains("record_game_session"),
    existingSession: true);
await Check("Gemini-limiet", new Plan(), t => true, false, true);
{
    var tools = new FakeTools();
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Olivier"], ["Jeroen"])] },
        new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Olivier"], ["Jeroen"])] });
    var processor = new CommandProcessor(tools, planner);
    var date = new DateOnly(2026, 11, 11);
    var first = await processor.RunAsync("Ik speelde Carcassonne met Jeroen; Olivier keek toe; Jeroen won.", date);
    var second = await processor.RunAsync("Olivier deed mee", date);
    if (!first.Message.Contains("Deed Olivier mee") || !tools.Calls.Contains("add_player")
        || tools.LastRecord?.GetProperty("players").GetArrayLength() != 2 || !second.Message.Contains("geregistreerd"))
        throw new Exception("De verduidelijking over een deelnemer is niet verwerkt.");
    passed++;
}
{
    var planner = new SequencePlanner(
        new Plan { Question = "Welk bordspel bedoel je?" },
        new Plan { Actions = [new ActionPlan { Kind = "get_players" }] });
    var processor = new CommandProcessor(new FakeTools(), planner);
    var date = new DateOnly(2026, 11, 12);
    await processor.RunAsync("Ik heb een potje gespeeld", date);
    var result = await processor.RunAsync("Welke spelers zijn er?", date);
    if (planner.Commands[1] != "Welke spelers zijn er?" || processor.PendingCommand is not null
        || !result.Message.StartsWith("Spelers:"))
        throw new Exception("Een nieuwe vraag bleef ten onrechte vastzitten aan een oude vervolgvraag.");
    passed++;
}
{
    var original = "Ik heb vandaag FreakyTown gespeeld met Jeroen en Olivier; Jeroen won.";
    var plannedSession = Session("FreakyTown", ["Jeroen", "Olivier"], ["Jeroen"]);
    var planner = new SequencePlanner(
        new Plan { Actions = [plannedSession] },
        new Plan { Actions = [Session("FreakyTown", ["Jeroen", "Olivier"], ["Jeroen"])] });
    var tools = new FakeTools();
    var processor = new CommandProcessor(tools, planner);
    var date = new DateOnly(2026, 11, 11);

    var first = await processor.RunAsync(original, date);
    if (!first.Message.Contains("minimumaantal") || processor.PendingCommand is null
        || tools.Calls.Any(call => call.StartsWith("add_") || call == "record_game_session"))
        throw new Exception($"Vervolgvraag is niet veilig: {first.Message}");

    var second = await processor.RunAsync("2 tot 4 spelers, 45 minuten", date);
    if (processor.PendingCommand is not null || !planner.Commands[1].Contains(original)
        || !tools.Calls.Contains("add_boardgame") || !tools.Calls.Contains("add_player")
        || tools.Calls.IndexOf("add_boardgame") > tools.Calls.IndexOf("add_player")
        || tools.Calls.IndexOf("add_player") > tools.Calls.IndexOf("record_game_session")
        || tools.LastRecord?.GetProperty("boardGameId").GetInt32() != 5
        || tools.LastRecord?.GetProperty("players")[1].GetProperty("playerId").GetInt32() != 4
        || !second.Message.Contains("geregistreerd"))
        throw new Exception($"Vervolg van potje is mislukt: {second.Message}");
    passed++;
}
{
    var original = "Ik speelde FreakyTown met Jeroen en Pallas; Jeroen won.";
    var planner = new SequencePlanner(
        new Plan { Question = "Wat zijn minimum, maximum en speelduur?" },
        new Plan { Actions = [Session("FreakyTown", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var processor = new CommandProcessor(new FakeTools(), planner);
    var first = await processor.RunAsync(original, new DateOnly(2026, 11, 11));
    var second = await processor.RunAsync("2 tot 4 spelers, 45 minuten", new DateOnly(2026, 11, 11));
    if (!first.Message.Contains("maximum") || !planner.Commands[1].Contains(original) || !second.Message.Contains("geregistreerd"))
        throw new Exception("Gemini-vraag verloor de oorspronkelijke registratie.");
    passed++;
}
await Check("Toon catalogus", new Plan { Actions = [new() { Kind = "get_boardgames" }, new() { Kind = "get_players" }, new() { Kind = "get_expansions" }] },
    t => t.Calls.SequenceEqual(["get_boardgames", "get_players", "get_game_expansions"]));
await Check("Alleen lezen blokkeert toevoegen", new Plan { Actions = [new() { Kind = "add_player", Name = "Kevin" }] },
    t => t.Calls.SequenceEqual(["get_boardgames", "get_players", "get_game_expansions"]), readOnly: true);
await Check("Alleen lezen voegt onbekende deelnemer niet toe", new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Olivier"], ["Jeroen"])] },
    t => t.Calls.SequenceEqual(["get_boardgames", "get_players", "get_game_expansions"]),
    readOnly: true, command: "Ik speelde Carcassonne met Jeroen en Olivier; Jeroen won.");
await Check("Alleen lezen blokkeert gemengd plan", new Plan { Actions = [new() { Kind = "get_players" }, new() { Kind = "delete_player", Id = 2 }] },
    t => t.Calls.SequenceEqual(["get_boardgames", "get_players", "get_game_expansions"]), readOnly: true);
foreach (var kind in new[] { "add_boardgame", "add_expansion", "record_session", "update_player", "update_boardgame", "update_expansion", "update_session", "delete_boardgame", "delete_expansion", "delete_session" })
    await Check($"Alleen lezen blokkeert {kind}", new Plan { Actions = [new() { Kind = kind }] },
        t => t.Calls.SequenceEqual(["get_boardgames", "get_players", "get_game_expansions"]), readOnly: true);
using (var http = new HttpClient(new LimitHandler()))
{
    var gemini = new GeminiPlanner(http, "mock-key");
    try { await gemini.PlanAsync("test", new Catalog([], [], []), new DateOnly(2026, 11, 11)); throw new Exception("HTTP 429 had moeten stoppen."); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("429") && gemini.RequestCount == 1) { passed++; }
}
{
    var handler = new UnavailableHandler();
    using var http = new HttpClient(handler);
    var gemini = new GeminiPlanner(http, "mock-key");
    var tools = new FakeTools();
    var processor = new CommandProcessor(tools, gemini);
    var result = await processor.RunAsync("Ik heb El Dorado gespeeld met Wim en Nick.", new DateOnly(2026, 11, 12));
    if (handler.Calls != 3 || result.GeminiRequests != 3 || processor.PendingCommand is null
        || tools.Calls.Contains("record_game_session")
        || !result.Message.Contains("Gemini is tijdelijk")) throw new Exception("Gemini 503 retry of contextbehoud faalde.");
    passed++;
}
{
    var handler = new RecoveringHandler();
    using var http = new HttpClient(handler);
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, new GeminiPlanner(http, "mock-key"))
        .RunAsync("Ik speelde Carcassonne met Pallas; Jeroen won.", new DateOnly(2026, 11, 11));
    if (handler.Calls != 3 || result.GeminiRequests != 3
        || tools.Calls.Count(call => call == "record_game_session") != 1)
        throw new Exception("Gemini-herstel registreerde het potje niet precies eenmaal.");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("FreakyTown | Publisher", "https://publisher.example/freakytown",
        "FreakyTown is for 2-4 players. Playing time: 45 minutes."));
    var firstPlan = new Plan { Actions = [Session("FreakyTown", ["Jeroen", "Pallas"], ["Jeroen"])] };
    var sourcedPlan = new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "FreakyTown", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 45 }, Session("FreakyTown", ["Jeroen", "Pallas"], ["Jeroen"])] };
    var planner = new SequencePlanner(firstPlan, sourcedPlan);
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Ik speelde FreakyTown met Pallas; ik won.", new DateOnly(2026, 11, 11));
    if (search.Calls != 1 || planner.RequestCount != 2 || result.GeminiRequests != 2
        || !tools.Calls.Contains("add_boardgame") || !tools.Calls.Contains("record_game_session")
        || !result.Message.Contains("Winnaar(s): Jeroen")
        || !result.Message.Contains("Spelers: 2–4") || !result.Message.Contains("Speelduur: 45 minuten")
        || !result.Message.Contains("[publisher.example](https://publisher.example/freakytown)"))
        throw new Exception($"Tavily-verrijking of bronvermelding mislukt: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("Monopoly | Publisher", "https://publisher.example/monopoly",
        "Monopoly playing time: 90 minutes."));
    var planner = new SequencePlanner(
        new Plan { Question = "Voor Monopoly ontbreekt nog speelduur in minuten. Wat zijn die gegevens?" },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly", MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 90 },
            Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools();
    var trace = new List<string>();
    var result = await new CommandProcessor(tools, planner, gameSearch: search, onTrace: trace.Add)
        .RunAsync("Registreer Monopoly met Jeroen en Pallas; Jeroen won vandaag. Minimaal 2 spelers, maximaal 8 spelers.", new DateOnly(2026, 11, 12));
    if (search.Calls != 1 || !search.LastMissing!.SequenceEqual(["speelduur"]) || planner.RequestCount != 2
        || !planner.Commands[1].Contains("90 minutes") || !tools.Calls.Contains("add_boardgame")
        || !tools.Calls.Contains("record_game_session") || !result.Message.Contains("Monopoly geregistreerd")
        || !result.Message.Contains("Speelduur: 90 minuten")
        || !result.Message.Contains("[publisher.example](https://publisher.example/monopoly)")
        || result.Message.Contains("Spelers: 2–8")
        || trace.All(line => !line.Contains("1 bronresultaten, bruikbare gegevens: speelduur")))
        throw new Exception($"Monopoly-speelduur werd niet automatisch gebruikt: {result.Message}");
    Console.WriteLine("Tavily-test: Monopoly board game playing time minutes -> 1 bron; 90 minuten gebruikt; MCP add_boardgame en record_game_session.");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("Monopoly | Publisher", "https://publisher.example/monopoly",
        "Monopoly is for 2-8 players. Playing time: 90 minutes."));
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"]) ] },
        new Plan { Question = "Voor Monopoly ontbreken nog minimumaantal spelers, maximumaantal spelers en speelduur." });
    var tools = new FakeTools();
    var trace = new List<string>();
    var processor = new CommandProcessor(tools, planner, gameSearch: search, onTrace: trace.Add);
    var result = await processor.RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 11));
    if (search.Calls != 1 || planner.RequestCount != 2
        || !planner.Commands[1].Contains("2-8 players") || !planner.Commands[1].Contains("90 minutes")
        || !tools.Calls.Contains("add_boardgame") || !tools.Calls.Contains("record_game_session")
        || processor.PendingCommand is not null || !result.Message.Contains("Monopoly geregistreerd")
        || trace.All(line => !line.Contains("bruikbare gegevens: minimumaantal spelers, maximumaantal spelers, speelduur")))
        throw new Exception($"Gemini-vraag overschreef complete Tavily-gegevens: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("Monopoly | Publisher", "https://publisher.example/monopoly",
        "Monopoly is for 2-8 players. Playing time: 90 minutes."));
    var planner = new SequencePlanner(
        new Plan { Question = "Voor Monopoly ontbreken minimumaantal spelers, maximumaantal spelers en speelduur." },
        new Plan { Question = "Wat zijn het spelersaantal en de speelduur van Monopoly?" },
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Registreer Monopoly met Jeroen en Pallas; Jeroen won vandaag.", new DateOnly(2026, 11, 11));
    if (planner.RequestCount != 3 || !planner.Commands[2].Contains("minimaal 2 spelers, maximaal 8 spelers, speelduur 90 minuten")
        || !result.Message.Contains("Monopoly geregistreerd") || !tools.Calls.Contains("record_game_session"))
        throw new Exception($"Gemini bleef gevonden spelgegevens opnieuw vragen: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("Monopoly | Publisher", "https://publisher.example/monopoly",
        "Monopoly is for 2-8 players. Playing time: 90 minutes."));
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"]) ] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly", MinPlayers = 3,
            MaxPlayers = 5, EstimatedMinutes = 120 }] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 11));
    if (!result.Message.Contains("Monopoly geregistreerd") || !tools.Calls.Contains("record_game_session")
        || tools.LastBoardgame?.GetProperty("minPlayers").GetInt32() != 2
        || tools.LastBoardgame?.GetProperty("maxPlayers").GetInt32() != 8
        || tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 90)
        throw new Exception($"Gemini liet het potje na Tavily weg: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(
        new GameSearchHit("FreakyTown | Publisher", "https://publisher.example/games/freakytown", "FreakyTown is for 2-4 players."),
        new GameSearchHit("FreakyTown | BoardGameGeek", "https://boardgamegeek.com/boardgame/123/freakytown", "FreakyTown playing time: 45 minutes."));
    var planner = new SequencePlanner(
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "FreakyTown" }] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "FreakyTown", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 45 }] });
    var result = await new CommandProcessor(new FakeTools(), planner, gameSearch: search)
        .RunAsync("Voeg bordspel FreakyTown toe.", new DateOnly(2026, 11, 11));
    if (search.Calls != 1 || !result.Message.Contains("Bronnen:")
        || !result.Message.Contains("[publisher.example](https://publisher.example/games/freakytown) (spelers)")
        || !result.Message.Contains("[BoardGameGeek](https://boardgamegeek.com/boardgame/123/freakytown) (speelduur)"))
        throw new Exception($"Meerdere bronpagina's werden niet afzonderlijk vermeld: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch();
    var planner = new SequencePlanner(
        new Plan { Question = "Voor Monopoly ontbreekt nog speelduur in minuten." },
        new Plan { Actions = [new() { Kind = "get_boardgames" }, new() { Kind = "get_players" }, new() { Kind = "get_players" }] });
    var processor = new CommandProcessor(new FakeTools(), planner, gameSearch: search);
    var first = await processor.RunAsync("Registreer Monopoly met Jeroen en Pallas; Jeroen won vandaag. Minimaal 2 spelers, maximaal 8 spelers.", new DateOnly(2026, 11, 12));
    var second = await processor.RunAsync("zoek ze op", new DateOnly(2026, 11, 12));
    if (search.Calls != 2 || !planner.Commands[1].Contains("Aanvullende gegevens: zoek ze op")
        || second.Message.Contains("Bordspellen:") || second.Message.Contains("Spelers:")
        || processor.PendingCommand is null)
        throw new Exception($"Vervolgvraag lekte cataloguslijsten: {first.Message} / {second.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("FreakyTown", "https://publisher.example/freakytown",
        "FreakyTown is for 2-4 players. Playing time: 45 minutes."));
    var planner = new SequencePlanner(new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "FreakyTown", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 45 }] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Voeg bordspel FreakyTown toe: 2-4 spelers, 45 minuten.", new DateOnly(2026, 11, 11));
    if (search.Calls != 0 || planner.RequestCount != 1 || !tools.Calls.Contains("add_boardgame")
        || result.Message.Contains("Bron:"))
        throw new Exception($"Gebruikersgegevens zijn niet hergebruikt: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("FreakyTown", "https://publisher.example/freakytown",
        "FreakyTown is for 2-4 players. Playing time is unknown."));
    var planner = new SequencePlanner(
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "FreakyTown" }] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "FreakyTown", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 45 }] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Voeg bordspel FreakyTown toe.", new DateOnly(2026, 11, 11));
    if (!result.Message.Contains("speelduur") || tools.Calls.Contains("add_boardgame"))
        throw new Exception($"Onbevestigde speelduur is opgeslagen: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch();
    var planner = new SequencePlanner(new Plan { Actions = [Session("Carcassonne", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var result = await new CommandProcessor(new FakeTools(), planner, gameSearch: search)
        .RunAsync("Ik speelde Carcassonne met Pallas; ik won.", new DateOnly(2026, 11, 11));
    if (search.Calls != 0 || planner.RequestCount != 1 || result.Message.Contains("Bron:"))
        throw new Exception("Bestaand spel veroorzaakte online zoekactie of bronvermelding.");
    passed++;
}
{
    var handler = new TavilyHandler();
    using var http = new HttpClient(handler);
    var search = new TavilyGameSearch(http, "mock-key");
    var hits = await search.SearchAsync("FreakyTown");
    if (handler.Calls != 2 || hits.Count != 1) throw new Exception("BGG-zoekactie en Tavily-fallback gaven onverwacht resultaat.");
    await search.SearchAsync("FreakyTown", ["speelduur"]);
    if (handler.Calls != 4 || handler.LastQuery != "FreakyTown board game playing time minutes")
        throw new Exception("Tavily zocht niet gericht naar speelduur.");
    await new TavilyGameSearch(http, "").SearchAsync("FreakyTown");
    if (handler.Calls != 4) throw new Exception("Ontbrekende Tavily-sleutel veroorzaakte een verzoek.");
    handler.TooManyRequests = true;
    try { await search.SearchAsync("FreakyTown"); throw new Exception("Tavily-limiet had moeten stoppen."); }
    catch (GameSearchUnavailableException ex) when (ex.Message.Contains("Tavily") && handler.Calls == 5) { }
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("El Dorado | Publisher", "https://publisher.example/el-dorado",
        "El Dorado is for 2-4 players. Playing time: 45 minutes."));
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("El Dorado", ["Jeroen", "Pallas"], ["Jeroen"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "The Quest for El Dorado", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 45 },
            Session("The Quest for El Dorado", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools { IncludeElDorado = false };
    var trace = new List<string>();
    var result = await new CommandProcessor(tools, planner, gameSearch: search, onTrace: trace.Add)
        .RunAsync("Ik speelde El Dorado met Pallas; ik won.", new DateOnly(2026, 11, 12));
    if (!result.Message.Contains("El Dorado geregistreerd") || !tools.Calls.Contains("record_game_session")
        || tools.LastBoardgame?.GetProperty("name").GetString() != "El Dorado"
        || trace.All(line => !line.Contains("gekoppeld aan gevraagde naam")))
        throw new Exception($"Uitgebreide El Dorado-titel blokkeerde registratie: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(
        new GameSearchHit("Monopoly | Source A", "https://publisher.example/monopoly", "Monopoly is for 2-8 players. Playing time: 45 minutes."),
        new GameSearchHit("Monopoly | Source B", "https://database.example/monopoly", "Monopoly playing time: 90 minutes."));
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly", MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 90 },
            Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools();
    var trace = new List<string>();
    var result = await new CommandProcessor(tools, planner, gameSearch: search, onTrace: trace.Add)
        .RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 12));
    if (tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 45
        || !tools.Calls.Contains("record_game_session")
        || trace.All(line => !line.Contains("Broncontrole Monopoly, speelduur: uitgever vermeldt een eenduidige waarde")))
        throw new Exception($"Uitgever kreeg geen voorrang boven afwijkende website: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("Monopoly | Source", "https://publisher.example/monopoly",
        "Monopoly is for 2-8 players. Playing time: 60-90 minutes."));
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly", MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 90 },
            Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 12));
    if (tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 90
        || !tools.Calls.Contains("record_game_session") || !result.Message.Contains("Speelduur: 90 minuten"))
        throw new Exception($"Bovengrens van speelduurrange werd niet als geschatte duur gebruikt: {result.Message}");
    passed++;
}
{
    var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
        ? [new GameSearchHit("Monopoly (1935) | BoardGameGeek", "https://boardgamegeek.com/boardgame/1406/monopoly",
            "Learn Monopoly in 5 minutes. 2-8 Players. Playing Time: 60-180 minutes.")]
        : []);
    using var http = new HttpClient(handler);
    var search = new TavilyGameSearch(http, "mock-key");
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly", MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 5 },
            Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (handler.Queries.Count != 1 || planner.RequestCount != 1
        || tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 180
        || !tools.Calls.Contains("record_game_session") || !result.Message.Contains("boardgamegeek.com/boardgame/1406/monopoly")
        || result.Message.Contains("5 minuten"))
        throw new Exception($"BGG Monopoly of speelduurrange faalde: {result.Message}");
    passed++;
}
{
    var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
        ? [new GameSearchHit("The Quest for El Dorado (2017) | BoardGameGeek", "https://boardgamegeek.com/boardgame/271615/the-quest-for-el-dorado",
            "The Quest for El Dorado: 2-4 Players. Playing Time: 45 Min.")]
        : []);
    using var http = new HttpClient(handler);
    var search = new TavilyGameSearch(http, "mock-key");
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("El Dorado", ["Jeroen", "Pallas"], ["Pallas"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "The Quest for El Dorado", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 45 },
            Session("The Quest for El Dorado", ["Jeroen", "Pallas"], ["Pallas"])] });
    var tools = new FakeTools { IncludeElDorado = false };
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Ik speelde El Dorado met Pallas; Pallas won.", new DateOnly(2026, 11, 13));
    if (handler.Queries.Count != 1 || planner.RequestCount != 1
        || !tools.Calls.Contains("record_game_session")
        || tools.LastBoardgame?.GetProperty("name").GetString() != "The Quest for El Dorado"
        || !result.Message.Contains("boardgamegeek.com/boardgame/271615/the-quest-for-el-dorado"))
        throw new Exception($"BGG El Dorado werd niet automatisch geregistreerd: {result.Message}");
    passed++;
}
{
    var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
        ? [new GameSearchHit("Monopoly Expansion | BoardGameGeek", "https://boardgamegeek.com/boardgameexpansion/42/monopoly-expansion",
                "2-8 Players. Playing Time: 5 Min."),
            new GameSearchHit("Monopoly: Star Wars Edition | BoardGameGeek", "https://boardgamegeek.com/boardgame/43/monopoly-star-wars-edition",
                "2-8 Players. Playing Time: 30 Min."),
            new GameSearchHit("Monopoly Junior | BoardGameGeek", "https://boardgamegeek.com/boardgame/44/monopoly-junior",
                "2-4 Players. Playing Time: 20 Min.")]
        : [new GameSearchHit("Monopoly | Official Publisher", "https://publisher.example/games/monopoly",
            "Players: 2-8. Playing time: 90 minutes.")]);
    using var http = new HttpClient(handler);
    var search = new TavilyGameSearch(http, "mock-key");
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly", MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 90 },
            Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (handler.Queries.Count != 2 || tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 90
        || !result.Message.Contains("publisher.example/games/monopoly")
        || result.Message.Contains("boardgamegeek.com/boardgame/43"))
        throw new Exception($"BGG-uitbreiding/editie werd gebruikt of publisher-fallback faalde: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(
        new GameSearchHit("Monopoly (1935) | BoardGameGeek", "https://boardgamegeek.com/boardgame/1406/monopoly",
            "2-8 Players. Playing Time: 60-180 minutes."),
        new GameSearchHit("Monopoly | Publisher", "https://publisher.example/monopoly",
            "2-8 players. Playing time: 90 minutes."));
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly", MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 90 },
            Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 180
        || !result.Message.Contains("boardgamegeek.com/boardgame/1406/monopoly"))
        throw new Exception($"BGG kreeg geen voorrang boven afwijkende publisher-waarde: {result.Message}");
    passed++;
}
{
    var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
        ? [new GameSearchHit("The Quest for El Dorado (2017) | BoardGameGeek", "https://boardgamegeek.com/boardgame/271615/the-quest-for-el-dorado",
            "The Quest for El Dorado: 2-4 Players.")]
        : [new GameSearchHit("El Dorado | Official Publisher", "https://publisher.example/el-dorado",
            "El Dorado: 3-5 players. Playing time: 45 minutes.")]);
    using var http = new HttpClient(handler);
    var search = new TavilyGameSearch(http, "mock-key");
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("El Dorado", ["Jeroen", "Pallas"], ["Pallas"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "El Dorado", MinPlayers = 3, MaxPlayers = 5, EstimatedMinutes = 45 },
            Session("El Dorado", ["Jeroen", "Pallas"], ["Pallas"])] });
    var tools = new FakeTools { IncludeElDorado = false };
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Ik speelde El Dorado met Pallas; Pallas won.", new DateOnly(2026, 11, 13));
    if (handler.Queries.Count != 2 || tools.LastBoardgame?.GetProperty("minPlayers").GetInt32() != 2
        || tools.LastBoardgame?.GetProperty("maxPlayers").GetInt32() != 4
        || tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 45
        || !result.Message.Contains("boardgamegeek.com/boardgame/271615/the-quest-for-el-dorado")
        || !result.Message.Contains("publisher.example/el-dorado"))
        throw new Exception($"BGG-spelers en publisher-speelduur werden niet gecombineerd: {result.Message}");
    passed++;
}
{
    var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
        ? [new GameSearchHit("Monopoly (1935) | BoardGameGeek", "https://boardgamegeek.com/boardgame/1406/monopoly",
            "2-8 Players.")]
        : query.Contains("official")
            ? [new GameSearchHit("Monopoly | Official Publisher", "https://publisher.example/monopoly",
                "Playing time: 90 minutes.")]
            : [new GameSearchHit("Monopoly tips", "https://articles.example/monopoly-tips",
                "Monopoly is for 2-8 players. Learn Monopoly in 5 minutes.")]);
    using var http = new HttpClient(handler);
    var hits = await new TavilyGameSearch(http, "mock-key").SearchAsync("Monopoly");
    var planner = new SequencePlanner(
        new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] },
        new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly", MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 5 },
            Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: new FakeGameSearch(hits.ToArray()))
        .RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (handler.Queries.Count != 3 || hits.Count != 2
        || tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 90
        || !result.Message.Contains("publisher.example/monopoly"))
        throw new Exception("Gerichte Tavily-herhaling na ontbrekende BGG-speelduur faalde.");
    passed++;
}
foreach (var (typed, canonical) in new[]
{
    ("pacu pacu", "Paku Paku"),
    ("castle of burguny", "The Castles of Burgundy"),
    ("castle of burgudny", "The Castles of Burgundy"),
    ("castle of burgundy", "The Castles of Burgundy"),
    ("TCoB", "The Castles of Burgundy"),
    ("El Dorado", "The Quest for El Dorado")
})
{
    var tools = new FakeTools { IncludeElDorado = false };
    tools.ExtraBoardgames.Add(new Item(20, canonical));
    var search = new FakeGameSearch();
    var planner = new SequencePlanner(new Plan { Actions = [
        new ActionPlan { Kind = "add_boardgame", Name = typed },
        Session(typed, ["Jeroen", "Pallas"], ["Jeroen"])
    ] });
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync($"Ik speelde {typed} met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (!result.Message.Contains($"Potje {canonical} geregistreerd") || search.Calls != 0
        || tools.Calls.Contains("add_boardgame") || tools.LastRecord?.GetProperty("boardGameId").GetInt32() != 20
        || planner.RequestCount != 1)
        throw new Exception($"Bestaand spel '{typed}' werd niet als '{canonical}' herkend: {result.Message}");
    passed++;
}
foreach (var (typed, canonical, slug) in new[]
{
    ("pacu pacu", "Paku Paku", "paku-paku"),
    ("castle of burguny", "The Castles of Burgundy", "the-castles-of-burgundy"),
    ("castle of burgundy", "The Castles of Burgundy", "the-castles-of-burgundy")
})
{
    var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
        ? [new GameSearchHit($"{canonical} (2011) | BoardGameGeek", $"https://boardgamegeek.com/boardgame/987/{slug}",
            $"{canonical}: 2-4 Players. Playing Time: 45 Min.")]
        : []);
    using var http = new HttpClient(handler);
    var tools = new FakeTools { IncludeElDorado = false };
    var planner = new SequencePlanner(new Plan { Actions = [Session(typed, ["Jeroen", "Pallas"], ["Jeroen"])] });
    var result = await new CommandProcessor(tools, planner, gameSearch: new TavilyGameSearch(http, "mock-key"))
        .RunAsync($"Ik speelde {typed} met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (handler.Queries.Count != 1 || planner.RequestCount != 1
        || tools.LastBoardgame?.GetProperty("name").GetString() != canonical
        || !tools.Calls.Contains("record_game_session") || !result.Message.Contains("boardgamegeek.com/boardgame/987"))
        throw new Exception($"BGG herkende '{typed}' niet als '{canonical}': {result.Message}");
    passed++;
}
{
    var tools = new FakeTools { IncludeElDorado = false };
    tools.ExtraBoardgames.AddRange([new Item(20, "Paku Paku"), new Item(21, "Paco Paco")]);
    var search = new FakeGameSearch();
    var result = await new CommandProcessor(tools,
        new SequencePlanner(new Plan { Actions = [Session("pacu pacu", ["Jeroen", "Pallas"], ["Jeroen"])] }),
        gameSearch: search).RunAsync("Ik speelde pacu pacu met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (!result.Message.Contains("Welk bordspel bedoel je") || search.Calls != 0
        || tools.Calls.Contains("record_game_session"))
        throw new Exception($"Dubbelzinnige naam werd niet verduidelijkt: {result.Message}");
    passed++;
}
{
    var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
        ? [new GameSearchHit("Paku Paku | BoardGameGeek", "https://boardgamegeek.com/boardgame/20/paku-paku",
                "2-4 Players. Playing Time: 15 Min."),
            new GameSearchHit("Paco Paco | BoardGameGeek", "https://boardgamegeek.com/boardgame/21/paco-paco",
                "2-4 Players. Playing Time: 15 Min.")]
        : []);
    using var http = new HttpClient(handler);
    var tools = new FakeTools { IncludeElDorado = false };
    var result = await new CommandProcessor(tools,
        new SequencePlanner(new Plan { Actions = [Session("pacu pacu", ["Jeroen", "Pallas"], ["Jeroen"])] }),
        gameSearch: new TavilyGameSearch(http, "mock-key"))
        .RunAsync("Ik speelde pacu pacu met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (handler.Queries.Count != 1 || !result.Message.Contains("Welk bordspel bedoel je")
        || tools.Calls.Contains("add_boardgame"))
        throw new Exception($"Twee aannemelijke BGG-spellen werden niet verduidelijkt: {result.Message}");
    passed++;
}
{
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools,
        new SequencePlanner(new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] }))
        .RunAsync("Ik speelde Qwizzle met Pallas; ik won.", new DateOnly(2026, 11, 13));
    if (!result.Message.Contains("Welk bordspel bedoel je") || tools.Calls.Contains("record_game_session"))
        throw new Exception("Gemini koppelde een andere spelnaam zonder aanwijzing in de opdracht.");
    passed++;
}
var root = Directory.GetCurrentDirectory();
var testDirectory = Path.Combine(root, ".tmp-ai-client-tests");
Directory.CreateDirectory(testDirectory);
var testDb = Path.Combine(testDirectory, $"meepleos-aiclient-test-{Guid.NewGuid():N}.db");
var oldDb = Environment.GetEnvironmentVariable("MEEPLEOS_DB_PATH");
try
{
    Environment.SetEnvironmentVariable("MEEPLEOS_DB_PATH", testDb);
    await using (var writable = await McpTools.ConnectAsync(root))
    {
        await writable.CallAsync("add_player", new { name = "Testspeler" });
        await writable.CallAsync("add_player", new { name = "Jeroen" });
        await writable.CallAsync("add_player", new { name = "Pallas" });
        await writable.CallAsync("add_boardgame", new { name = "Monopoly", minPlayers = 2, maxPlayers = 8, estimatedMinutes = 90 });
        await writable.CallAsync("add_boardgame", new { name = "Freakytown", minPlayers = 2, maxPlayers = 4, estimatedMinutes = 45 });
        var planner = new SequencePlanner(new Plan { Question = "Wie won?" },
            new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] },
            new Plan { Actions = [Session("Freakytown", ["Jeroen", "Pallas"], ["Pallas"])] });
        var search = new FakeGameSearch();
        var integrationErrors = new List<Exception>();
        var processor = new CommandProcessor(writable, planner, gameSearch: search, onError: integrationErrors.Add);
        var day = new DateOnly(2026, 11, 11);
        await processor.RunAsync("Ik heb El Dorado gespeeld.", day);
        var monopoly = await processor.RunAsync("Ik wil Monopoly registreren met Jeroen en Pallas; Jeroen won vandaag.", day);
        var freakytown = await processor.RunAsync("Kun je Freakytown registreren met Jeroen en Pallas; Pallas won vandaag?", day);
        if (!monopoly.Message.Contains("Monopoly geregistreerd") || !freakytown.Message.Contains("Freakytown geregistreerd")
            || planner.Commands[1].Contains("El Dorado") || planner.Commands[2].Contains("El Dorado")
            || search.Calls != 0 || processor.PendingCommand is not null)
            throw new Exception($"Opeenvolgende registraties op de echte testdatabase faalden: {monopoly.Message} / {freakytown.Message}. {string.Join(" | ", integrationErrors)}");
        passed++;

        var sourcedSearch = new FakeGameSearch(new GameSearchHit("Testopoly | Publisher", "https://publisher.example/testopoly",
            "Testopoly: 2-8 players, 90 minutes."));
        var sourcedPlanner = new SequencePlanner(
            new Plan { Actions = [Session("Testopoly", ["Jeroen", "Pallas"], ["Jeroen"]) ] },
            new Plan { Question = "Wat zijn het spelersaantal en de speelduur van Testopoly?" });
        var sourcedProcessor = new CommandProcessor(writable, sourcedPlanner, gameSearch: sourcedSearch);
        var sourcedResult = await sourcedProcessor.RunAsync("Ik speelde Testopoly met Pallas; ik won.", day);
        var savedGames = await writable.CallAsync("get_boardgames");
        var savedGame = savedGames.EnumerateArray().Single(game => game.GetProperty("name").GetString() == "Testopoly");
        var savedSessions = await writable.CallAsync("get_game_sessions", new { boardGameId = savedGame.GetProperty("id").GetInt32() });
        if (!sourcedResult.Message.Contains("Testopoly geregistreerd") || sourcedProcessor.PendingCommand is not null
            || savedGame.GetProperty("minPlayers").GetInt32() != 2
            || savedGame.GetProperty("maxPlayers").GetInt32() != 8
            || savedGame.GetProperty("estimatedMinutes").GetInt32() != 90
            || savedSessions.GetArrayLength() != 1)
            throw new Exception($"Tavily-registratie op de testdatabase faalde: {sourcedResult.Message}");
        passed++;
    }
    await using (var readonlyTools = await McpTools.ConnectAsync(root, readOnly: true))
    {
        var players = await readonlyTools.CallAsync("get_players");
        if (!players.EnumerateArray().Any(p => p.GetProperty("name").GetString() == "Testspeler")) throw new Exception("Testdatabase is niet leesbaar.");
        try { await readonlyTools.CallAsync("add_player", new { name = "Verboden" }); throw new Exception("Schrijven was toegestaan."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("geblokkeerd")) { }
    }
    var clientDll = Path.Combine(root, "src", "MeepleOS.AiClient", "bin", "Release", "net10.0", "MeepleOS.AiClient.dll");
    if (File.Exists(clientDll))
    {
        using var process = new System.Diagnostics.Process { StartInfo = new("dotnet") { Arguments = $"\"{clientDll}\" --read-only --list", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root } };
        process.StartInfo.Environment["MEEPLEOS_DB_PATH"] = testDb;
        process.StartInfo.Environment.Remove("GEMINI_API_KEY");
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !output.Contains("Testspeler") || !output.Contains("Bordspellen:") || !output.Contains("Uitbreidingen:"))
            throw new Exception($"Alleen-lezenlijst mislukt: {error} {output}");
    }
    passed++;
}
finally
{
    Environment.SetEnvironmentVariable("MEEPLEOS_DB_PATH", oldDb);
    foreach (var suffix in new[] { "", "-wal", "-shm" })
        if (File.Exists(testDb + suffix)) File.Delete(testDb + suffix);
}
await CheckGeminiLedger(testDirectory);
Console.WriteLine($"{passed} tests geslaagd; 0 echte Gemini-verzoeken. Integratietest gebruikte een aparte tijdelijke database.");

async Task CheckGeminiLedger(string directory)
{
    if (RequestLedger.DefaultPerMinute != 15 || RequestLedger.DefaultPerDay != 500) throw new Exception("Onjuiste standaardlimieten.");
    var instant = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    var minutePath = Path.Combine(directory, $"gemini-minute-{Guid.NewGuid():N}.json");
    var dayPath = Path.Combine(directory, $"gemini-day-{Guid.NewGuid():N}.json");
    var noKeyPath = Path.Combine(directory, $"gemini-no-key-{Guid.NewGuid():N}.json");
    try
    {
        var handler = new PlanHandler();
        using var http = new HttpClient(handler);
        var planner = new GeminiPlanner(http, "mock-key", ledger: new RequestLedger(minutePath, 15, 500, () => instant));
        var catalog = new Catalog([], [], []);
        var today = new DateOnly(2026, 11, 11);
        for (var i = 0; i < 15; i++) await planner.PlanAsync("test", catalog, today);
        if (handler.Calls != 15 || planner.RequestCount != 15 || CountLedger(minutePath) != 15) throw new Exception("De eerste 15 verzoeken zijn niet juist geteld.");
        try { await planner.PlanAsync("test", catalog, today); throw new Exception("Verzoek 16 had geblokkeerd moeten worden."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("per minuut")) { }
        if (handler.Calls != 15 || CountLedger(minutePath) != 15) throw new Exception("Een geblokkeerd verzoek is geteld.");
        instant = instant.AddMinutes(1);
        await planner.PlanAsync("test", catalog, today);
        if (handler.Calls != 16 || CountLedger(minutePath) != 16) throw new Exception("Het minuutvenster is niet op tijd verlopen.");
        passed++;

        var mcp = new FakeTools();
        await mcp.CallAsync("get_boardgames");
        await mcp.CallAsync("get_players");
        await mcp.CallAsync("get_game_expansions");
        if (handler.Calls != 16 || CountLedger(minutePath) != 16) throw new Exception("MCP-leesacties verhoogden de Gemini-teller.");
        var commandResult = await new CommandProcessor(mcp, planner).RunAsync("toon spelers", today);
        if (commandResult.GeminiRequests != 1 || handler.Calls != 17 || CountLedger(minutePath) != 17 || mcp.Calls.Count != 6)
            throw new Exception("Een opdracht telde MCP-aanroepen als Gemini-verzoeken.");
        passed++;

        instant = new DateTimeOffset(2026, 11, 11, 22, 59, 0, TimeSpan.Zero); // 23:59 in Amsterdam
        var dayHandler = new PlanHandler();
        using var dayHttp = new HttpClient(dayHandler);
        var dayPlanner = new GeminiPlanner(dayHttp, "mock-key", ledger: new RequestLedger(dayPath, 1000, 500, () => instant));
        for (var i = 0; i < 500; i++) await dayPlanner.PlanAsync("test", catalog, today);
        try { await dayPlanner.PlanAsync("test", catalog, today); throw new Exception("Verzoek 501 had geblokkeerd moeten worden."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("per dag")) { }
        if (dayHandler.Calls != 500 || CountLedger(dayPath) != 500) throw new Exception("De daglimiet telt onjuist.");
        instant = instant.AddMinutes(1); // 00:00 in Amsterdam
        await dayPlanner.PlanAsync("test", catalog, today.AddDays(1));
        if (dayHandler.Calls != 501 || CountLedger(dayPath) != 501) throw new Exception("De Amsterdamse daggrens is onjuist.");
        passed++;

        var noKeyHandler = new PlanHandler();
        using var noKeyHttp = new HttpClient(noKeyHandler);
        var noKeyPlanner = new GeminiPlanner(noKeyHttp, "", ledger: new RequestLedger(noKeyPath, 15, 500));
        try { await noKeyPlanner.PlanAsync("test", catalog, today); throw new Exception("Een ontbrekende sleutel had moeten stoppen."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("GEMINI_API_KEY")) { }
        if (noKeyHandler.Calls != 0 || noKeyPlanner.RequestCount != 0 || File.Exists(noKeyPath)) throw new Exception("Een niet-verstuurd verzoek is geteld.");
        passed++;
    }
    finally
    {
        foreach (var path in new[] { minutePath, dayPath, noKeyPath }) if (File.Exists(path)) File.Delete(path);
    }
}

static int CountLedger(string path) => JsonSerializer.Deserialize<List<DateTimeOffset>>(File.ReadAllText(path))!.Count;

async Task RunSourceTests(bool includeMcp)
{
    var count = 0;
    async Task Registration(string typed, string expectedName, GameSearchHit[] hits,
        int min, int max, int minutes, params string[] links)
    {
        var tools = new FakeTools { IncludeElDorado = false };
        var planner = new SequencePlanner(
            new Plan { Actions = [Session(typed, ["Jeroen", "Pallas"], ["Jeroen"])] },
            new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = typed,
                MinPlayers = 9, MaxPlayers = 10, EstimatedMinutes = 999 },
                Session(typed, ["Jeroen", "Pallas"], ["Jeroen"])] });
        var search = new FakeGameSearch(hits);
        var result = await new CommandProcessor(tools, planner, gameSearch: search)
            .RunAsync($"Ik speelde {typed} met Pallas; ik won.", new DateOnly(2026, 11, 11));
        if (search.Calls != 1 || !tools.Calls.Contains("record_game_session")
            || tools.LastBoardgame?.GetProperty("name").GetString() != expectedName
            || tools.LastBoardgame?.GetProperty("minPlayers").GetInt32() != min
            || tools.LastBoardgame?.GetProperty("maxPlayers").GetInt32() != max
            || tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != minutes
            || links.Any(link => !result.Message.Contains(link)))
            throw new Exception($"Bronkeuze voor {typed} faalde: {result.Message}");
        count++;
    }

    await Registration("Cabanga", "Cabanga", [
        new("Cabanga | AMIGO", "https://amigo-spiele.de/cabanga", "Players: 3-6. Playing time: 20 minutes."),
        new("Cabanga | Guide", "https://guide.example/cabanga", "2-8 players. Playing time: 45 minutes.")
    ], 3, 6, 20, "amigo-spiele.de/cabanga");

    await Registration("Monopoly", "Monopoly", [
        new("Monopoly (1935) | BoardGameGeek", "https://boardgamegeek.com/boardgame/1406/monopoly",
            "2-8 Players. Playing Time: 60-180 minutes."),
        new("Monopoly | Publisher", "https://publisher.example/monopoly",
            "2-8 players. Playing time: 90 minutes.")
    ], 2, 8, 180, "boardgamegeek.com/boardgame/1406/monopoly");

    await Registration("El Dorado", "The Quest for El Dorado", [
        new("The Quest for El Dorado (2017) | BoardGameGeek",
            "https://boardgamegeek.com/boardgame/271615/the-quest-for-el-dorado", "2-4 Players."),
        new("El Dorado | Official Publisher", "https://publisher.example/el-dorado",
            "Players: 3-5. Playing time: 45 minutes.")
    ], 2, 4, 45, "boardgamegeek.com/boardgame/271615/the-quest-for-el-dorado", "publisher.example/el-dorado");

    await Registration("Paku Paku", "Paku Paku", [
        new("Paku Paku | Guide", "https://guide.example/paku-paku",
            "2-4 players, 5 minutes."),
        new("Paku Paku | Rules", "https://rules.example/paku-paku",
            "Players: 2-4. Playing time: 15 minutes.")
    ], 2, 4, 15, "rules.example/paku-paku");

    await Registration("Cabanga", "Cabanga", [
        new("Cabanga | Database A", "https://db-a.example/cabanga", "Players: 3-6. Playing time: 20 minutes."),
        new("Cabanga | Database B", "https://db-b.example/cabanga", "Players: 3-6. Playing time: 20 minutes."),
        new("Cabanga | Guide", "https://guide.example/cabanga", "Players: 2-8. Playing time: 45 minutes.")
    ], 3, 6, 20, "db-a.example/cabanga");

    await Registration("Cabanga", "Cabanga", [
        new("Cabanga | AMIGO", "https://amigo-spiele.de/cabanga",
            "Cabanga supports 3-6 players. A round takes about 20 minutes.")
    ], 3, 6, 20, "amigo-spiele.de/cabanga");

    {
        var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
            ? [new GameSearchHit("Dice Feast | BoardGameGeek",
                "https://boardgamegeek.com/boardgame/20/dice-feast",
                "Also known as: Paku Paku. 2-4 Players. Playing Time: 15 minutes.")]
            : []);
        using var http = new HttpClient(handler);
        var hits = await new TavilyGameSearch(http, "mock-key").SearchAsync("Paku Paku");
        if (hits.Count != 1 || handler.Queries.Count != 1)
            throw new Exception("Alternatieve BGG-titel werd afgewezen.");
        count++;
    }

    {
        var tools = new FakeTools { IncludeElDorado = false };
        var search = new FakeGameSearch(new GameSearchHit("Monopoly | Publisher",
            "https://publisher.example/monopoly", "Players: 2-8. Expect a 90-minute playtime."));
        var planner = new SequencePlanner(
            new Plan { Actions = [Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] },
            new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly",
                MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 90 },
                Session("Monopoly", ["Jeroen", "Pallas"], ["Jeroen"])] });
        var result = await new CommandProcessor(tools, planner, gameSearch: search)
            .RunAsync("Ik speelde Monopoly met Pallas; ik won.", new DateOnly(2026, 11, 11));
        if (!tools.Calls.Contains("record_game_session")
            || tools.LastBoardgame?.GetProperty("estimatedMinutes").GetInt32() != 90
            || !result.Message.Contains("publisher.example/monopoly"))
            throw new Exception($"Onderbouwde Gemini-speelduur werd genegeerd: {result.Message}");
        count++;
    }

    {
        var tools = new FakeTools();
        var search = new FakeGameSearch();
        var result = await new CommandProcessor(tools,
            new SequencePlanner(new Plan { Actions = [Session("El Dorado", ["Jeroen", "Pallas"], ["Jeroen"])] }),
            gameSearch: search).RunAsync("Ik speelde El Dorado met Pallas; ik won.", new DateOnly(2026, 11, 11));
        if (search.Calls != 0 || !result.Message.Contains("De Zoektocht naar El Dorado geregistreerd"))
            throw new Exception($"Bestaand spel werd niet hergebruikt: {result.Message}");
        count++;
    }

    {
        var tools = new FakeTools { IncludeElDorado = false };
        var search = new FakeGameSearch(new GameSearchHit("Cabanga | Publisher",
            "https://publisher.example/cabanga", "Cabanga: 3-6 players. Playing time: 20 minutes."));
        var planner = new SequencePlanner(
            new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Cabanga", NoMaximum = true }] },
            new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Cabanga", NoMaximum = true,
                MinPlayers = 3, EstimatedMinutes = 20 }] });
        var result = await new CommandProcessor(tools, planner, gameSearch: search)
            .RunAsync("Registreer Cabanga met minimaal 3 spelers en geen vast maximum.", new DateOnly(2026, 11, 11));
        if (tools.LastBoardgame?.GetProperty("maxPlayers").ValueKind != JsonValueKind.Null
            || !result.Message.Contains("Cabanga toegevoegd"))
            throw new Exception($"Gebruikersopgave zonder maximum werd overschreven: {result.Message}");
        count++;
    }

    {
        var tools = new FakeTools { IncludeElDorado = false };
        tools.ExtraBoardgames.AddRange([new Item(20, "Paku Paku"), new Item(21, "Paco Paco")]);
        var planner = new SequencePlanner(new Plan { Actions = [Session("pacu pacu", ["Jeroen", "Pallas"], ["Jeroen"])] });
        var result = await new CommandProcessor(tools, planner, gameSearch: new FakeGameSearch())
            .RunAsync("Ik speelde pacu pacu met Pallas; ik won.", new DateOnly(2026, 11, 11));
        if (planner.RequestCount != 1 || !result.Message.Contains("Welk bordspel bedoel je")
            || tools.Calls.Contains("record_game_session"))
            throw new Exception($"Dubbelzinnig spel werd niet veilig verduidelijkt: {result.Message}");
        count++;
    }

    {
        var tools = new FakeTools();
        var session = Session("Carcassonne", ["Jeroen", "Pallas"], [], ["De Rivier"]);
        var planner = new SequencePlanner(
            new Plan { Question = "Wie won?" },
            new Plan { Actions = [session] });
        var processor = new CommandProcessor(tools, planner);
        var first = await processor.RunAsync("Ik speelde Carcassonne met Pallas en de uitbreiding De Rivier. Scores in die volgorde 20 en 30.",
            new DateOnly(2026, 11, 11));
        var second = await processor.RunAsync("Pallas won.", new DateOnly(2026, 11, 11));
        var record = tools.LastRecord;
        if (!first.Message.Contains("Wie won") || processor.PendingCommand is not null
            || !second.Message.Contains("Carcassonne geregistreerd")
            || record?.GetProperty("winnerPlayerIds").EnumerateArray().Single().GetInt32() != 3
            || record?.GetProperty("expansionIds").EnumerateArray().Single().GetInt32() != 7
            || record?.GetProperty("players").EnumerateArray().Last().GetProperty("score").GetInt32() != 30)
            throw new Exception($"Vervolgrepliek, scores, winnaar of uitbreiding gingen verloren: {second.Message}");
        count++;
    }

    {
        var tools = new FakeTools { IncludeElDorado = false };
        var search = new FakeGameSearch(
            new GameSearchHit("Cabanga | Guide A", "https://guide-a.example/cabanga",
                "Players: 3-6. Playing time: 20 minutes."),
            new GameSearchHit("Cabanga | Guide B", "https://guide-b.example/cabanga",
                "Players: 3-6. Playing time: 30 minutes."));
        var planner = new SequencePlanner(
            new Plan { Actions = [Session("Cabanga", ["Jeroen", "Pallas"], ["Jeroen"])] },
            new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Cabanga",
                MinPlayers = 3, MaxPlayers = 6, EstimatedMinutes = 30 }] });
        var result = await new CommandProcessor(tools, planner, gameSearch: search)
            .RunAsync("Ik speelde Cabanga met Pallas; ik won.", new DateOnly(2026, 11, 11));
        if (!result.Message.Contains("speelduur") || tools.Calls.Contains("add_boardgame")
            || planner.RequestCount != 2) throw new Exception("Onbeslist conflict of Gemini-waarde werd opgeslagen.");
        count++;
    }

    {
        var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
            ? [new GameSearchHit("Paku Paku Expansion | BoardGameGeek",
                    "https://boardgamegeek.com/boardgameexpansion/20/paku-paku-expansion", "2-4 Players. Playing Time: 5 Min."),
                new GameSearchHit("Paku Paku Junior | BoardGameGeek",
                    "https://boardgamegeek.com/boardgame/21/paku-paku-junior", "2-4 Players. Playing Time: 10 Min.")]
            : [new GameSearchHit("Paku Paku | Official Publisher", "https://publisher.example/paku-paku",
                "Players: 2-4. Playing time: 15 minutes.")]);
        using var http = new HttpClient(handler);
        var hits = await new TavilyGameSearch(http, "mock-key").SearchAsync("Paku Paku");
        if (handler.Queries.Count != 2 || hits.Count != 1 || !hits[0].Url.Contains("publisher.example"))
            throw new Exception("Uitbreiding of Junior-editie kreeg voorrang boven het basisspel.");
        count++;
    }

    {
        var page = new GameSearchHit("Monopoly (1935) | BoardGameGeek",
            "https://boardgamegeek.com/boardgame/1406/monopoly", "2-8 Players.");
        var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
            ? [page]
            : [page with { Content = "Playing Time: 90 minutes." }]);
        using var http = new HttpClient(handler);
        var hits = await new TavilyGameSearch(http, "mock-key").SearchAsync("Monopoly");
        if (handler.Queries.Count != 2 || hits.Count != 1 || !hits[0].Content.Contains("90 minutes"))
            throw new Exception("Een rijker fragment van dezelfde BGG-pagina werd genegeerd of extra gezocht.");
        count++;
    }

    {
        var handler = new ScriptedTavilyHandler(query => query.Contains("site:boardgamegeek.com")
            ? [new GameSearchHit("Monopoly (1935) | BoardGameGeek",
                    "https://boardgamegeek.com/boardgame/1406/monopoly", "2-8 Players.")]
            : [new GameSearchHit("Monopoly (2024) | BoardGameGeek",
                    "https://boardgamegeek.com/boardgame/2406/monopoly", "2-8 Players. Playing Time: 60 Min.")]);
        using var http = new HttpClient(handler);
        try
        {
            await new TavilyGameSearch(http, "mock-key").SearchAsync("Monopoly");
            throw new Exception("Twee BGG-edities met dezelfde titel werden samengevoegd.");
        }
        catch (Exception ex) when (ex.Message.Contains("Welke editie van Monopoly")) { }
        if (handler.Queries.Count != 2) throw new Exception("Editie-ambiguïteit werd in latere resultaten gemist.");
        count++;
    }

    if (includeMcp)
    {
        var root = Directory.GetCurrentDirectory();
        var database = Path.Combine(root, ".tmp-ai-client-tests", $"source-selection-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        try
        {
            await using var mcp = await McpTools.ConnectAsync(root, databasePath: database);
            await mcp.CallAsync("add_player", new { name = "Jeroen" });
            await mcp.CallAsync("add_player", new { name = "Pallas" });
            var search = new FakeGameSearch(
                new GameSearchHit("Cabanga | AMIGO", "https://amigo-spiele.de/cabanga",
                    "Players: 3-6. Playing time: 20 minutes."),
                new GameSearchHit("Cabanga | Guide", "https://guide.example/cabanga",
                    "2-8 players. Playing time: 45 minutes."));
            var planner = new SequencePlanner(
                new Plan { Actions = [Session("Cabanga", ["Jeroen", "Pallas"], ["Jeroen"])] },
                new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Cabanga",
                    MinPlayers = 2, MaxPlayers = 8, EstimatedMinutes = 45 },
                    Session("Cabanga", ["Jeroen", "Pallas"], ["Jeroen"])] });
            var result = await new CommandProcessor(mcp, planner, gameSearch: search)
                .RunAsync("Ik speelde Cabanga met Pallas; ik won.", new DateOnly(2026, 11, 11));
            var games = await mcp.CallAsync("get_boardgames");
            var cabanga = games.EnumerateArray().Single(game => game.GetProperty("name").GetString() == "Cabanga");
            var sessions = await mcp.CallAsync("get_game_sessions", new { boardGameId = cabanga.GetProperty("id").GetInt32() });
            if (!result.Message.Contains("Potje Cabanga geregistreerd")
                || cabanga.GetProperty("minPlayers").GetInt32() != 3
                || cabanga.GetProperty("maxPlayers").GetInt32() != 6
                || cabanga.GetProperty("estimatedMinutes").GetInt32() != 20
                || sessions.GetArrayLength() != 1)
                throw new Exception($"Cabanga werd niet correct op de testdatabase geregistreerd: {result.Message}");
            count++;
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(database + suffix)) File.Delete(database + suffix);
        }
    }

    Console.WriteLine($"{count} gerichte brontests geslaagd; geen productiedatabase of echte API-verzoeken.");
}

async Task Check(string label, Plan plan, Func<FakeTools, bool> verify, bool failRecord = false, bool limit = false, bool readOnly = false, string? command = null, bool existingSession = false)
{
    var tools = new FakeTools { FailRecord = failRecord, ExistingSession = existingSession };
    var planner = new FakePlanner(plan, limit);
    var result = await new CommandProcessor(tools, planner, readOnly).RunAsync(command ?? label, new DateOnly(2026, 11, 11));
    if (!verify(tools) || planner.Calls != 1 || result.GeminiRequests != 1
        || (failRecord && (!result.Message.Contains("Wel opgeslagen: Speler Kevin toegevoegd") || result.Message.Contains("Potje Carcassonne geregistreerd")))
        || (limit && !result.Message.Contains("limiet")) || (readOnly && !result.Message.Contains("Alleen-lezenstand")))
        throw new Exception($"Test mislukt: {label}: {result.Message}");
    passed++;
}

static ActionPlan Session(string game, string[] players, string[] winners, string[]? expansions = null) => new()
{
    Kind = "record_session", Boardgame = game, PlayedAt = "2026-11-11",
    Players = players.Select(x => new ParticipantPlan { Name = x }).ToList(), Winners = [.. winners], Expansions = expansions is null ? [] : [.. expansions]
};

sealed class FakePlanner(Plan plan, bool limit) : IPlanner
{
    public int Calls { get; private set; }

    public Task<Plan> PlanAsync(string command, Catalog catalog, DateOnly today, CancellationToken ct = default)
    {
        Calls++;
        return limit ? throw new InvalidOperationException("Gemini API-limiet bereikt (HTTP 429).") : Task.FromResult(plan);
    }
}

sealed class SequencePlanner(params Plan[] plans) : IPlanner, IRequestCounter
{
    private readonly Queue<Plan> remaining = new(plans);
    public List<string> Commands { get; } = [];
    public int RequestCount => Commands.Count;

    public Task<Plan> PlanAsync(string command, Catalog catalog, DateOnly today, CancellationToken ct = default)
    {
        Commands.Add(command);
        return Task.FromResult(remaining.Dequeue());
    }
}

sealed class FakeTools : IMeepleTools
{
    public List<string> Calls { get; } = [];
    public JsonElement? LastRecord { get; private set; }
    public JsonElement? LastBoardgame { get; private set; }
    public bool FailRecord { get; set; }
    public bool ExistingSession { get; set; }
    public bool IncludeElDorado { get; set; } = true;
    public List<Item> ExtraBoardgames { get; } = [];
    public Task<JsonElement> CallAsync(string tool, object? arguments = null, CancellationToken ct = default)
    {
        Calls.Add(tool);
        var args = arguments is null ? JsonDocument.Parse("{}").RootElement : JsonSerializer.SerializeToElement(arguments);
        object response = tool switch
        {
            "get_boardgames" => (IncludeElDorado
                ? new[] { new { id = 1, name = "Carcassonne" }, new { id = 8, name = "Munchkin" }, new { id = 10, name = "De Zoektocht naar El Dorado" } }
                : new[] { new { id = 1, name = "Carcassonne" }, new { id = 8, name = "Munchkin" } })
                .Concat(ExtraBoardgames.Select(game => new { id = game.Id, name = game.Name })).ToArray(),
            "get_players" => new[] { new { id = 2, name = "Jeroen" }, new { id = 3, name = "Pallas" }, new { id = 6, name = "Roan" }, new { id = 11, name = "Wim" }, new { id = 12, name = "Nick" } },
            "get_game_expansions" => new[] { new { id = 7, name = "De Rivier", boardGameId = 1 } },
            "get_game_sessions" => ExistingSession
                ? new object[] { new { boardGameId = 1, playedAt = "2026-11-11", players = new[] { new { playerId = 2 }, new { playerId = 3 } } } }
                : Array.Empty<object>(),
            "add_player" => new { id = 4, name = args.GetProperty("name").GetString() },
            "add_boardgame" => new { id = 5, name = args.GetProperty("name").GetString() },
            "add_game_expansion" => new { id = 10, name = args.GetProperty("name").GetString() },
            "record_game_session" => new { id = 9 },
            _ => new { id = 10, name = "Uitbreiding" }
        };
        if (tool == "record_game_session")
        {
            if (FailRecord) throw new InvalidOperationException("MCP schrijffout");
            LastRecord = args;
        }
        if (tool == "add_boardgame") LastBoardgame = args;
        return Task.FromResult(JsonSerializer.SerializeToElement(response));
    }
}

sealed class LimitHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests));
}

sealed class UnavailableHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }
}

sealed class RecoveringHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (Calls < 3) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        var plan = JsonSerializer.Serialize(new Plan { Actions = [new ActionPlan { Kind = "record_session", Boardgame = "Carcassonne", PlayedAt = "2026-11-11", Players = [new() { Name = "Jeroen" }, new() { Name = "Pallas" }], Winners = ["Jeroen"] }] });
        var response = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + JsonSerializer.Serialize(plan) + "}]}}]}";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(response) });
    }
}

sealed class FakeGameSearch(params GameSearchHit[] hits) : IGameSearch
{
    public int Calls { get; private set; }
    public IReadOnlyCollection<string>? LastMissing { get; private set; }
    public Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts, CancellationToken ct = default)
    {
        LastMissing = missingFacts;
        return SearchAsync(game, ct);
    }
    public Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<GameSearchHit>>(hits);
    }
}

sealed class TavilyHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? LastQuery { get; private set; }
    public bool TooManyRequests { get; set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (request.RequestUri?.AbsoluteUri != "https://api.tavily.com/search"
            || request.Headers.Authorization?.Scheme != "Bearer"
            || request.Headers.Authorization.Parameter != "mock-key") throw new Exception("Onjuist Tavily-endpoint of authenticatie.");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        LastQuery = body.RootElement.GetProperty("query").GetString();
        if (body.RootElement.GetProperty("search_depth").GetString() != "basic"
            || body.RootElement.GetProperty("include_answer").GetBoolean()
            || body.RootElement.GetProperty("include_raw_content").GetBoolean()
            || body.RootElement.GetProperty("auto_parameters").GetBoolean())
            throw new Exception("Tavily gebruikt meer dan de gratis basic Search-functie.");
        if (TooManyRequests) return new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"results\":[{\"title\":\"FreakyTown publisher homepage\",\"url\":\"https://publisher.example/\",\"content\":\"FreakyTown 2-4 players, 45 minutes\"},{\"title\":\"FreakyTown\",\"url\":\"https://publisher.example/freakytown\",\"content\":\"FreakyTown 2-4 players, 45 minutes\"}]}")
        };
    }
}

sealed class ScriptedTavilyHandler(Func<string, GameSearchHit[]> results) : HttpMessageHandler
{
    public List<string> Queries { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var query = body.RootElement.GetProperty("query").GetString()!;
        Queries.Add(query);
        var payload = JsonSerializer.Serialize(new { results = results(query).Select(hit => new
        {
            title = hit.Title, url = hit.Url, content = hit.Content
        }) });
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(payload) };
    }
}

sealed class PlanHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.RequestUri!.AbsoluteUri.Contains("gemini-3.5-flash-lite")) throw new Exception("Het Gemini-model is gewijzigd.");
        Calls++;
        var response = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = "{\"question\":null,\"actions\":[]}" } } } } } });
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(response) });
    }
}

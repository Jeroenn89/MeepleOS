using System.Text.Json;
using MeepleOS.AiClient;

var passed = 0;
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
        || !result.Message.Contains("Winnaar(s): Jeroen")) throw new Exception($"Tavily-verrijking mislukt: {result.Message}");
    passed++;
}
{
    var search = new FakeGameSearch(new GameSearchHit("FreakyTown", "https://publisher.example/freakytown",
        "FreakyTown is for 2-4 players. Playing time: 45 minutes."));
    var planner = new SequencePlanner(new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "FreakyTown", MinPlayers = 2, MaxPlayers = 4, EstimatedMinutes = 45 }] });
    var tools = new FakeTools();
    var result = await new CommandProcessor(tools, planner, gameSearch: search)
        .RunAsync("Voeg bordspel FreakyTown toe: 2-4 spelers, 45 minuten.", new DateOnly(2026, 11, 11));
    if (search.Calls != 0 || planner.RequestCount != 1 || !tools.Calls.Contains("add_boardgame"))
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
    await new CommandProcessor(new FakeTools(), planner, gameSearch: search)
        .RunAsync("Ik speelde Carcassonne met Pallas; ik won.", new DateOnly(2026, 11, 11));
    if (search.Calls != 0 || planner.RequestCount != 1) throw new Exception("Bestaand spel veroorzaakte online zoekactie.");
    passed++;
}
{
    var handler = new TavilyHandler();
    using var http = new HttpClient(handler);
    var search = new TavilyGameSearch(http, "mock-key");
    var hits = await search.SearchAsync("FreakyTown");
    if (handler.Calls != 1 || hits.Count != 1) throw new Exception("Tavily basic Search gaf onverwacht resultaat.");
    await new TavilyGameSearch(http, "").SearchAsync("FreakyTown");
    if (handler.Calls != 1) throw new Exception("Ontbrekende Tavily-sleutel veroorzaakte een verzoek.");
    handler.TooManyRequests = true;
    if ((await search.SearchAsync("FreakyTown")).Count != 0 || handler.Calls != 2)
        throw new Exception("Tavily-limiet veroorzaakte een herhaling of betaalde fallback.");
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
        await writable.CallAsync("add_player", new { name = "Testspeler" });
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
    public bool FailRecord { get; set; }
    public bool ExistingSession { get; set; }
    public Task<JsonElement> CallAsync(string tool, object? arguments = null, CancellationToken ct = default)
    {
        Calls.Add(tool);
        var args = arguments is null ? JsonDocument.Parse("{}").RootElement : JsonSerializer.SerializeToElement(arguments);
        object response = tool switch
        {
            "get_boardgames" => new[] { new { id = 1, name = "Carcassonne" }, new { id = 8, name = "Munchkin" } },
            "get_players" => new[] { new { id = 2, name = "Jeroen" }, new { id = 3, name = "Pallas" }, new { id = 6, name = "Roan" } },
            "get_game_expansions" => new[] { new { id = 7, name = "De Rivier", boardGameId = 1 } },
            "get_game_sessions" => ExistingSession
                ? new object[] { new { players = new[] { new { playerId = 2 }, new { playerId = 3 } } } }
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
        return Task.FromResult(JsonSerializer.SerializeToElement(response));
    }
}

sealed class LimitHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests));
}

sealed class FakeGameSearch(params GameSearchHit[] hits) : IGameSearch
{
    public int Calls { get; private set; }
    public Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<GameSearchHit>>(hits);
    }
}

sealed class TavilyHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public bool TooManyRequests { get; set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (request.RequestUri?.AbsoluteUri != "https://api.tavily.com/search"
            || request.Headers.Authorization?.Scheme != "Bearer"
            || request.Headers.Authorization.Parameter != "mock-key") throw new Exception("Onjuist Tavily-endpoint of authenticatie.");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        if (body.RootElement.GetProperty("search_depth").GetString() != "basic"
            || body.RootElement.GetProperty("include_answer").GetBoolean()
            || body.RootElement.GetProperty("include_raw_content").GetBoolean()
            || body.RootElement.GetProperty("auto_parameters").GetBoolean())
            throw new Exception("Tavily gebruikt meer dan de gratis basic Search-functie.");
        if (TooManyRequests) return new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"results\":[{\"title\":\"FreakyTown\",\"url\":\"https://publisher.example/freakytown\",\"content\":\"FreakyTown 2-4 players, 45 minutes\"}]}")
        };
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

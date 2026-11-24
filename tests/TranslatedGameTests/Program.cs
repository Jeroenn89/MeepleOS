using System.Net;
using System.Text.Json;
using MeepleOS.AiClient;

using (var geminiHandler = new GeminiHandler())
using (var geminiHttp = new HttpClient(geminiHandler))
{
    var firstPlan = await new GeminiPlanner(geminiHttp, "mock-key").PlanAsync(
        "Voeg Regenwormen toe", new Catalog([], [], []), Today());
    Check(firstPlan.AlternativeGameName == "Pick-omino"
        && geminiHandler.Prompt!.Contains("alternativeGameName")
        && geminiHandler.SchemaHasAlternativeName,
        "Gemini kan de alternatieve titel bij de eerste aanroep meegeven");
}

await CheckTranslated("Regenwormen", "Pick-omino", "https://boardgamegeek.com/boardgame/15818/pick-omino", 2, 7, 20);
await CheckTranslated("De Kolonisten van Catan", "Catan", "https://boardgamegeek.com/boardgame/13/catan", 3, 4, 90);

var duplicateTools = new FakeTools("Pickomino");
var duplicateSearch = new FakeSearch();
var duplicate = await new CommandProcessor(duplicateTools,
    new FakeGemini(new Plan { AlternativeGameName = "Pick-omino", Actions = [Add("Regenwormen")] }),
    gameSearch: duplicateSearch).RunAsync("Voeg Regenwormen toe", Today());
Check(duplicateTools.AddedName is null && duplicateSearch.Games.Count == 0
    && duplicate.Message == "Geen actie nodig.", "Internationale titel in collectie wordt hergebruikt");

var localTools = new FakeTools("Regenwormen");
var localSearch = new FakeSearch();
await new CommandProcessor(localTools,
    new FakeGemini(new Plan { AlternativeGameName = "Pick-omino", Actions = [Add("Regenwormen")] }),
    gameSearch: localSearch).RunAsync("Voeg Regenwormen toe", Today());
Check(localTools.AddedName is null && localSearch.Games.Count == 0,
    "Nederlandse titel in collectie wordt hergebruikt");

using (var handler = new SearchHandler("Regenwormen", "Pick-omino", 2, 7, 20, aliasBridge: false))
using (var http = new HttpClient(handler))
{
    var search = new TavilyGameSearch(http, "mock-key");
    var hits = await search.SearchAsync("Regenwormen", "Pick-omino",
        ["minimumaantal spelers", "maximumaantal spelers", "speelduur"], null, []);
    Check(hits.Count == 0 && handler.Queries.Count == 3
        && handler.Queries.Distinct().Count() == 3,
        "Onbewezen alternatieve titel wordt niet opgeslagen of herhaald gezocht");
}

Console.WriteLine("Vertaalde bordspellen: Regenwormen, Catan, bestaande collectie en broncontrole geslaagd.");

static async Task CheckTranslated(string dutch, string international, string sourceUrl,
    int min, int max, int minutes)
{
    using var handler = new SearchHandler(dutch, international, min, max, minutes, aliasBridge: true);
    using var http = new HttpClient(handler);
    var tools = new FakeTools();
    var gemini = new FakeGemini(
        new Plan { AlternativeGameName = international, Actions = [Add(dutch)] },
        new Plan { Actions = [Add(dutch)] });
    var result = await new CommandProcessor(tools, gemini,
        gameSearch: new TavilyGameSearch(http, "mock-key"))
        .RunAsync($"Voeg het bordspel {dutch} toe", Today());

    Check(tools.AddedName == dutch && tools.MinPlayers == min && tools.MaxPlayers == max
        && tools.Minutes == minutes, $"{dutch} wordt onder de Nederlandse titel opgeslagen: {result.Message}");
    Check(result.Message.Contains($"{dutch} toegevoegd") && result.Message.Contains(sourceUrl),
        $"Bevestiging van {dutch} bevat de gebruikte bron");
    Check(handler.Queries.Count == 2 && handler.Queries.Distinct().Count() == 2
        && handler.Queries[0].Contains($"\"{dutch}\" bordspel spelers speelduur")
        && handler.Queries[1].Contains($"\"{international}\" board game players playing time minutes"),
        $"{dutch} gebruikt één Nederlandse en één internationale zoekopdracht");
}

static DateOnly Today() => new(2026, 11, 16);
static ActionPlan Add(string name) => new() { Kind = "add_boardgame", Name = name };
static void Check(bool condition, string label)
{
    if (!condition) throw new Exception($"Test mislukt: {label}");
}

sealed class FakeGemini(params Plan[] plans) : IPlanner
{
    private int index;
    public Task<Plan> PlanAsync(string command, Catalog catalog, DateOnly today, CancellationToken ct = default)
        => Task.FromResult(plans[index++]);
}

sealed class FakeSearch : IGameSearch
{
    public List<string> Games { get; } = [];
    public Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default)
    {
        Games.Add(game);
        return Task.FromResult<IReadOnlyList<GameSearchHit>>([]);
    }
}

sealed class FakeTools(params string[] existingGames) : IMeepleTools
{
    public string? AddedName { get; private set; }
    public int? MinPlayers { get; private set; }
    public int? MaxPlayers { get; private set; }
    public int? Minutes { get; private set; }

    public Task<JsonElement> CallAsync(string tool, object? arguments = null, CancellationToken ct = default)
    {
        var input = arguments is null ? JsonSerializer.SerializeToElement(new { })
            : JsonSerializer.SerializeToElement(arguments);
        object result = tool switch
        {
            "get_my_account" => new { username = "test", playerId = (int?)null, playerName = (string?)null },
            "find_boardgame_in_catalog" => null!,
            "get_boardgames" => existingGames.Select((name, index) => new { id = index + 1, name }).ToArray(),
            "get_players" or "get_game_expansions" => Array.Empty<object>(),
            "add_boardgame" => new { id = 99, name = input.GetProperty("name").GetString() },
            _ => throw new Exception($"Onverwachte MCP-aanroep: {tool}")
        };
        if (tool == "add_boardgame")
        {
            AddedName = input.GetProperty("name").GetString();
            MinPlayers = input.GetProperty("minPlayers").GetInt32();
            MaxPlayers = input.GetProperty("maxPlayers").GetInt32();
            Minutes = input.GetProperty("estimatedMinutes").GetInt32();
        }
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }
}

sealed class SearchHandler(string dutch, string international, int min, int max, int minutes,
    bool aliasBridge) : HttpMessageHandler
{
    public List<string> Queries { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.AbsolutePath == "/extract")
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"results\":[]}") };
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var query = body.RootElement.GetProperty("query").GetString()!;
        Queries.Add(query);
        object[] results;
        if (query.Contains($"\"{dutch}\" bordspel"))
            results = aliasBridge
                ? [new { title = $"{dutch} | Uitgever", url = $"https://publisher.example/{Uri.EscapeDataString(dutch)}",
                    content = $"{dutch} heet internationaal {international}." }]
                : [];
        else if (query.Contains($"\"{international}\" board game"))
            results = [new { title = international, url = international == "Catan"
                    ? "https://boardgamegeek.com/boardgame/13/catan"
                    : "https://boardgamegeek.com/boardgame/15818/pick-omino",
                content = $"Players: {min}-{max}. Playing time: {minutes} minutes." }];
        else results = [];
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { results }))
        };
    }
}

sealed class GeminiHandler : HttpMessageHandler
{
    public string? Prompt { get; private set; }
    public bool SchemaHasAlternativeName { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        Prompt = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0]
            .GetProperty("text").GetString();
        var schema = body.RootElement.GetProperty("generationConfig").GetProperty("responseJsonSchema");
        SchemaHasAlternativeName = schema.GetProperty("properties").TryGetProperty("alternativeGameName", out _);
        var plan = JsonSerializer.Serialize(new Plan
        {
            AlternativeGameName = "Pick-omino",
            Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Regenwormen" }]
        });
        var response = new { candidates = new[] { new { content = new { parts = new[] { new { text = plan } } } } } };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(response))
        };
    }
}

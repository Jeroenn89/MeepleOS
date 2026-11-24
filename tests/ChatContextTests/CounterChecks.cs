using System.Net;
using System.Text.Json;
using MeepleOS.AiClient;
using MeepleOS.Web.Services;

static class CounterChecks
{
    public static async Task RunAsync()
    {
        var today = new DateOnly(2026, 11, 16);
        var tools = new CounterTools();
        var conversation = new ChatConversation();

        using var unusedHttp = new HttpClient(new TavilyHandler());
        var unusedSearch = new TavilyGameSearch(unusedHttp, "mock-key");
        var readPlanner = new CounterPlanner(new Plan { Actions = [new ActionPlan { Kind = "get_boardgames" }] });
        var read = await new CommandProcessor(tools, readPlanner, gameSearch: unusedSearch)
            .RunAsync("Staat Carcassonne in mijn collectie?", today);
        var readExchange = conversation.Begin("Staat Carcassonne in mijn collectie?");
        conversation.Complete(readExchange, read.Message, readPlanner.RequestCount, unusedSearch.RequestCount);
        Check(readExchange.Response!.Contains("Carcassonne") && readExchange.GeminiRequests == 1
            && readExchange.TavilyRequests == 0 && !readExchange.Response.Contains("Gemini-verzoeken"),
            "Bestaand spel toont één Gemini-verzoek en nul Tavily-verzoeken buiten het antwoord");

        using var searchHandler = new TavilyHandler();
        using var searchHttp = new HttpClient(searchHandler);
        var search = new TavilyGameSearch(searchHttp, "mock-key");
        var addPlanner = new CounterPlanner(new Plan
        {
            Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly" }]
        });
        var added = await new CommandProcessor(tools, addPlanner, gameSearch: search)
            .RunAsync("Voeg Monopoly toe", today);
        var addExchange = conversation.Begin("Voeg Monopoly toe");
        conversation.Complete(addExchange, added.Message, addPlanner.RequestCount, search.RequestCount);
        Check(added.Message.Contains("Monopoly toegevoegd") && addExchange.GeminiRequests == 1
            && addExchange.TavilyRequests == searchHandler.Calls && addExchange.TavilyRequests > 0,
            "Zoekopdracht toont het aantal werkelijk verzonden Tavily-verzoeken");

        using var failedHandler = new TavilyHandler(fail: true);
        using var failedHttp = new HttpClient(failedHandler);
        var failedSearch = new TavilyGameSearch(failedHttp, "mock-key");
        await new CommandProcessor(new CounterTools(),
            new CounterPlanner(new Plan { Actions = [new ActionPlan { Kind = "add_boardgame", Name = "Monopoly" }] }),
            gameSearch: failedSearch).RunAsync("Voeg Monopoly toe", today);
        Check(failedHandler.Calls == 1 && failedSearch.RequestCount == 1,
            "Ook een mislukte Tavily-poging telt mee");

        using var geminiHandler = new RetryGeminiHandler();
        using var geminiHttp = new HttpClient(geminiHandler);
        var gemini = new GeminiPlanner(geminiHttp, "mock-key");
        await gemini.PlanAsync("Staat Carcassonne in mijn collectie?", new Catalog([], [], []), today);
        Check(geminiHandler.Calls == 3 && gemini.RequestCount == 3,
            "Gemini telt twee mislukte pogingen en de geslaagde nieuwe poging");

        Console.WriteLine("Chattellers: bestaand spel 1/0, Tavily-zoekactie en mislukte pogingen geslaagd.");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception($"Test mislukt: {label}");
    }

    private sealed class CounterPlanner(Plan plan) : IPlanner, IRequestCounter
    {
        public int RequestCount { get; private set; }
        public Task<Plan> PlanAsync(string command, Catalog catalog, DateOnly today, CancellationToken ct = default)
        {
            RequestCount++;
            return Task.FromResult(plan);
        }
    }

    private sealed class CounterTools : IMeepleTools
    {
        public Task<JsonElement> CallAsync(string tool, object? arguments = null, CancellationToken ct = default)
        {
            object result = tool switch
            {
                "get_my_account" => new { username = "test", playerId = (int?)null, playerName = (string?)null },
                "get_boardgames" => new[] { new { id = 1, name = "Carcassonne" } },
                "find_boardgame_in_catalog" => null!,
                "get_players" or "get_game_expansions" => Array.Empty<object>(),
                "add_boardgame" => new { id = 2, name = "Monopoly" },
                _ => throw new Exception($"Onverwachte MCP-aanroep: {tool}")
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }

    private sealed class TavilyHandler(bool fail = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (fail)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var results = new[] { new
            {
                title = "Monopoly (1935)",
                url = "https://boardgamegeek.com/boardgame/1406/monopoly",
                content = "Players: 2-8. Playing time: 90 minutes."
            } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { results }))
            });
        }
    }

    private sealed class RetryGeminiHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Calls < 3)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var plan = JsonSerializer.Serialize(new Plan { Actions = [new ActionPlan { Kind = "get_boardgames" }] });
            var response = new { candidates = new[] { new { content = new { parts = new[] { new { text = plan } } } } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response))
            });
        }
    }
}

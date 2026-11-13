using MeepleOS.AiClient;

namespace MeepleOS.Web.Services;

public sealed class ChatService(WebOptions options, IHttpClientFactory httpFactory, ILogger<ChatService> logger)
{
    private string? pendingCommand;
    private string? pendingQuestion;
    private IReadOnlyList<GameSearchHit> pendingChoices = [];
    private string? pendingSearchName;
    private IReadOnlyCollection<int> rejectedBggIds = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string> AskAsync(string command)
    {
        await gate.WaitAsync();
        try { return await AskCoreAsync(command); }
        finally { gate.Release(); }
    }

    private async Task<string> AskCoreAsync(string command)
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return "AI-chat is niet beschikbaar: GEMINI_API_KEY ontbreekt. Handmatige invoer werkt wel.";
        try
        {
            await using var tools = await McpTools.ConnectAsync(options.Root, databasePath: options.DatabasePath);
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            var ledger = new RequestLedger(Path.Combine(options.Root, "data", "gemini-requests.json"), 15, 500);
            var planner = new GeminiPlanner(http, key, ledger: ledger);
            using var searchHttp = httpFactory.CreateClient();
            searchHttp.Timeout = TimeSpan.FromSeconds(20);
            var search = new TavilyGameSearch(searchHttp, Environment.GetEnvironmentVariable("TAVILY_API_KEY") ?? "",
                detail => logger.LogInformation("AI-chat: {Detail}", detail));
            var processor = new CommandProcessor(tools, planner, pendingCommand: pendingCommand, gameSearch: search,
                onError: error => logger.LogError(error, "AI-chat fout."),
                onTrace: detail => logger.LogInformation("AI-chat: {Detail}", detail),
                pendingQuestion: pendingQuestion, pendingChoices: pendingChoices,
                pendingSearchName: pendingSearchName, rejectedBggIds: rejectedBggIds);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
            var result = await processor.RunAsync(command, today);
            pendingCommand = processor.PendingCommand;
            pendingQuestion = processor.PendingQuestion;
            pendingChoices = processor.PendingChoices;
            pendingSearchName = processor.PendingSearchName;
            rejectedBggIds = processor.RejectedBggIds;
            return $"{result.Message}{Environment.NewLine}Gemini-verzoeken: {result.GeminiRequests}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI-chat kon de opdracht niet afronden.");
            return "De MCP-server is tijdelijk niet beschikbaar. Je kunt de registratie later in dit gesprek vervolgen.";
        }
    }
}

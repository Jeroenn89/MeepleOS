using MeepleOS.AiClient;

namespace MeepleOS.Web.Services;

public sealed class ChatService(WebOptions options, IHttpClientFactory httpFactory, ILogger<ChatService> logger)
{
    public ChatConversation Conversation { get; } = new();

    private string? pendingCommand;
    private string? pendingQuestion;
    private IReadOnlyList<GameSearchHit> pendingChoices = [];
    private string? pendingSearchName;
    private IReadOnlyCollection<int> rejectedBggIds = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task AskAsync(ChatExchange exchange)
    {
        await gate.WaitAsync();
        try
        {
            var answer = await AskCoreAsync(exchange.Request);
            Conversation.Complete(exchange, answer.Message, answer.GeminiRequests, answer.TavilyRequests);
        }
        finally { gate.Release(); }
    }

    private async Task<(string Message, int GeminiRequests, int TavilyRequests)> AskCoreAsync(string command)
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            return ("AI-chat is niet beschikbaar: GEMINI_API_KEY ontbreekt. Handmatige invoer werkt wel.", 0, 0);

        GeminiPlanner? planner = null;
        TavilyGameSearch? search = null;
        try
        {
            await using var tools = await McpTools.ConnectAsync(options.Root, databasePath: options.DatabasePath);
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            var ledger = new RequestLedger(Path.Combine(options.Root, "data", "gemini-requests.json"), 15, 500);
            planner = new GeminiPlanner(http, key, ledger: ledger);
            using var searchHttp = httpFactory.CreateClient();
            searchHttp.Timeout = TimeSpan.FromSeconds(20);
            search = new TavilyGameSearch(searchHttp, Environment.GetEnvironmentVariable("TAVILY_API_KEY") ?? "",
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
            return (result.Message, planner.RequestCount, search.RequestCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI-chat kon de opdracht niet afronden.");
            return ("De MCP-server is tijdelijk niet beschikbaar. Je kunt de registratie later in dit gesprek vervolgen.",
                planner?.RequestCount ?? 0, search?.RequestCount ?? 0);
        }
    }
}

using MeepleOS.AiClient;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.DataLayer.Data.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace MeepleOS.Web.Services;

public sealed class ChatService(WebOptions options, IHttpClientFactory httpFactory, ILogger<ChatService> logger,
    IAccountContext account, IDataProtectionProvider protection, UserManager<MeepleUser> users)
{
    public ChatConversation Conversation { get; } = new();

    private string? pendingCommand;
    private string? pendingQuestion;
    private IReadOnlyList<GameSearchHit> pendingChoices = [];
    private string? pendingSearchName;
    private IReadOnlyCollection<int> rejectedBggIds = [];
    private IReadOnlyList<ActionPlan> pendingActions = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task AskAsync(ChatExchange exchange)
    {
        await gate.WaitAsync();
        try
        {
            var answer = await AskCoreAsync(exchange.Request);
            Conversation.Complete(exchange, answer.Message, answer.GeminiRequests, answer.TavilyRequests,
                answer.InputExample);
        }
        finally { gate.Release(); }
    }

    private async Task<(string Message, int GeminiRequests, int TavilyRequests, string? InputExample)> AskCoreAsync(string command)
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            return ("Meeple Companion is niet beschikbaar: GEMINI_API_KEY ontbreekt. Handmatige invoer werkt wel.", 0, 0, null);

        GeminiPlanner? planner = null;
        TavilyGameSearch? search = null;
        try
        {
            var accountId = await account.GetAccountIdAsync();
            var user = await users.FindByIdAsync(accountId) ?? throw new UnauthorizedAccessException();
            var token = protection.CreateProtector("MeepleOS.McpAccount.v1")
                .ToTimeLimitedDataProtector().Protect(new McpAuthPayload(accountId, user.SecurityStamp!).Serialize(), TimeSpan.FromMinutes(10));
            await using var tools = await McpTools.ConnectAsync(options.Root, databasePath: options.DatabasePath,
                authToken: token, requestId: Guid.NewGuid());
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
                pendingSearchName: pendingSearchName, rejectedBggIds: rejectedBggIds, pendingActions: pendingActions);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
            var result = await processor.RunAsync(command, today);
            pendingCommand = processor.PendingCommand;
            pendingQuestion = processor.PendingQuestion;
            pendingChoices = processor.PendingChoices;
            pendingSearchName = processor.PendingSearchName;
            rejectedBggIds = processor.RejectedBggIds;
            pendingActions = processor.PendingActions;
            return (result.Message, planner.RequestCount, search.RequestCount, result.InputExample);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI-chat kon de opdracht niet afronden.");
            return ("De MCP-server is tijdelijk niet beschikbaar. Je kunt de registratie later in dit gesprek vervolgen.",
                planner?.RequestCount ?? 0, search?.RequestCount ?? 0, null);
        }
    }
}

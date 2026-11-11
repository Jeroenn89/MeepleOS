using MeepleOS.AiClient;

namespace MeepleOS.Web.Services;

public sealed class ChatService(WebOptions options, IHttpClientFactory httpFactory)
{
    private string? pendingCommand;

    public async Task<string> AskAsync(string command)
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return "AI-chat is niet beschikbaar: GEMINI_API_KEY ontbreekt. Handmatige invoer werkt wel.";
        try
        {
            await using var tools = await McpTools.ConnectAsync(options.Root);
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            var ledger = new RequestLedger(Path.Combine(options.Root, "data", "gemini-requests.json"), 15, 500);
            var planner = new GeminiPlanner(http, key, ledger: ledger);
            var processor = new CommandProcessor(tools, planner, pendingCommand: pendingCommand);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
            var result = await processor.RunAsync(command, today);
            pendingCommand = processor.PendingCommand;
            return result.Message;
        }
        catch (Exception ex) { return $"AI-chat is tijdelijk niet beschikbaar: {ex.Message}"; }
    }
}

namespace MeepleOS.Web.Services;

public sealed class ChatExchange(string request)
{
    public string Request { get; } = request;
    public string? Response { get; private set; }
    public int GeminiRequests { get; private set; }
    public int TavilyRequests { get; private set; }

    internal void SetResponse(string response, int geminiRequests, int tavilyRequests)
    {
        Response = response;
        GeminiRequests = geminiRequests;
        TavilyRequests = tavilyRequests;
    }
}

public sealed class ChatConversation
{
    private readonly List<ChatExchange> exchanges = [];

    public IReadOnlyList<ChatExchange> Exchanges => exchanges;
    public event Action? Changed;

    public ChatExchange Begin(string request)
    {
        var exchange = new ChatExchange(request);
        exchanges.Add(exchange);
        Changed?.Invoke();
        return exchange;
    }

    public void Complete(ChatExchange exchange, string response, int geminiRequests = 0, int tavilyRequests = 0)
    {
        if (!exchanges.Contains(exchange))
            throw new ArgumentException("Het verzoek hoort niet bij dit gesprek.", nameof(exchange));

        exchange.SetResponse(response, geminiRequests, tavilyRequests);
        Changed?.Invoke();
    }
}

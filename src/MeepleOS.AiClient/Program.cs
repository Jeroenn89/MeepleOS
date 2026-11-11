using MeepleOS.AiClient;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
if (!File.Exists(Path.Combine(root, "MeepleOS.slnx"))) root = Directory.GetCurrentDirectory();
var readOnly = args.Contains("--read-only");
if (args.Any(a => a is not ("--read-only" or "--list" or "--check-connect")))
{
    Console.Error.WriteLine("Onbekende optie. Gebruik --read-only, --list of --check-connect.");
    Environment.ExitCode = 2;
    return;
}
if (args.Contains("--list") && !readOnly)
{
    Console.Error.WriteLine("Gebruik --list samen met --read-only.");
    Environment.ExitCode = 2;
    return;
}
if (args.Contains("--check-connect"))
{
    try
    {
        await using var readTools = await McpTools.ConnectAsync(root, readOnly: true);
        foreach (var name in new[] { "get_boardgames", "get_players", "get_game_expansions", "get_game_sessions" })
        {
            var rows = await readTools.CallAsync(name);
            Console.WriteLine($"{name}: {rows.GetArrayLength()} records gelezen.");
        }
    }
    catch (Exception ex) { Console.Error.WriteLine($"MCP-leescontrole mislukt: {ex.Message}"); Environment.ExitCode = 1; }
    return;
}
if (args.Contains("--list"))
{
    try
    {
        await using var readTools = await McpTools.ConnectAsync(root, readOnly: true);
        foreach (var (tool, title) in new[] { ("get_boardgames", "Bordspellen"), ("get_players", "Spelers"), ("get_game_expansions", "Uitbreidingen") })
        {
            var rows = await readTools.CallAsync(tool);
            Console.WriteLine($"{title}: {rows}");
        }
    }
    catch (Exception ex) { Console.Error.WriteLine($"Lezen mislukt: {ex.Message}"); Environment.ExitCode = 1; }
    return;
}
var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("GEMINI_API_KEY ontbreekt. Stel deze omgevingsvariabele veilig in en start opnieuw.");
    return;
}
static int Limit(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var n) && n > 0 ? n : fallback;
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
try
{
    await using var tools = await McpTools.ConnectAsync(root, readOnly);
    var ledger = new RequestLedger(Path.Combine(root, "data", "gemini-requests.json"), Limit("GEMINI_RPM_LIMIT", RequestLedger.DefaultPerMinute), Limit("GEMINI_RPD_LIMIT", RequestLedger.DefaultPerDay));
    var planner = new GeminiPlanner(http, key, readOnly, ledger);
    var processor = new CommandProcessor(tools, planner, readOnly);
    Console.WriteLine(readOnly ? "MeepleOS AI (alleen lezen). Typ een vraag, of 'stop'." : "MeepleOS AI. Typ een opdracht, of 'stop'.");
    while (true)
    {
        Console.Write("> ");
        var command = Console.ReadLine();
        if (command is null || command.Equals("stop", StringComparison.OrdinalIgnoreCase)) break;
        if (string.IsNullOrWhiteSpace(command)) continue;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var result = await processor.RunAsync(command, today);
        Console.WriteLine(result.Message);
        Console.WriteLine($"Gemini-verzoeken voor deze opdracht: {result.GeminiRequests}");
    }
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }

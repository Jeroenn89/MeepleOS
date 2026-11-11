using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MeepleOS.AiClient;

public sealed class McpTools : IMeepleTools, IAsyncDisposable
{
    private static readonly HashSet<string> ReadTools = ["get_boardgames", "get_players", "get_game_expansions", "get_game_sessions", "get_player_statistics", "get_boardgame_statistics", "get_leaderboard", "get_play_statistics"];
    private readonly McpClient client;
    private readonly bool readOnly;

    private McpTools(McpClient client, bool readOnly) { this.client = client; this.readOnly = readOnly; }

    public static async Task<McpTools> ConnectAsync(string root, bool readOnly = false, CancellationToken ct = default)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var server = Environment.GetEnvironmentVariable("MEEPLEOS_SERVER_DLL")
            ?? Path.Combine(root, "src", "MeepleOS.ServerLayer", "bin", configuration, "net10.0", "MeepleOS.ServerLayer.dll");
        if (!File.Exists(server)) throw new FileNotFoundException("Bouw eerst MeepleOS.slnx; de MCP-server ontbreekt.", server);
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet", Arguments = [server], WorkingDirectory = root,
            InheritEnvironmentVariables = true,
            EnvironmentVariables = new Dictionary<string, string?> { ["GEMINI_API_KEY"] = null, ["MEEPLEOS_READ_ONLY"] = readOnly ? "1" : "0" }
        });
        return new McpTools(await McpClient.CreateAsync(transport, cancellationToken: ct), readOnly);
    }

    public async Task<JsonElement> CallAsync(string tool, object? arguments = null, CancellationToken ct = default)
    {
        if (readOnly && !ReadTools.Contains(tool)) throw new InvalidOperationException($"Alleen-lezenstand: MCP-actie {tool} is geblokkeerd.");
        var map = arguments is null ? new Dictionary<string, object?>() :
            JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments))!;
        var result = await client.CallToolAsync(tool, map, cancellationToken: ct);
        if (result.IsError == true) throw new InvalidOperationException($"MCP {tool}: {string.Join(" ", result.Content.OfType<TextContentBlock>().Select(x => x.Text))}");
        var content = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException($"MCP {tool} gaf geen leesbaar resultaat.");
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}

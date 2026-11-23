using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MeepleOS.AiClient;

public sealed class McpTools : IMeepleTools, IAsyncDisposable
{
    private static readonly HashSet<string> ReadTools = ["get_boardgames", "get_players", "get_game_expansions", "get_game_sessions", "get_player_statistics", "get_boardgame_statistics", "get_leaderboard", "get_play_statistics"];
    private readonly McpClient client;
    private readonly bool readOnly;
    private readonly string temporaryServerDirectory;

    private McpTools(McpClient client, bool readOnly, string temporaryServerDirectory)
    { this.client = client; this.readOnly = readOnly; this.temporaryServerDirectory = temporaryServerDirectory; }

    public static async Task<McpTools> ConnectAsync(string root, bool readOnly = false, CancellationToken ct = default, string? databasePath = null, string? authToken = null)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var server = Environment.GetEnvironmentVariable("MEEPLEOS_SERVER_DLL")
            ?? Path.Combine(root, "src", "MeepleOS.ServerLayer", "bin", configuration, "net10.0", "MeepleOS.ServerLayer.dll");
        if (!File.Exists(server)) throw new FileNotFoundException("Bouw eerst MeepleOS.slnx; de MCP-server ontbreekt.", server);
        // Windows locks loaded assemblies. Run each MCP child from a temporary snapshot so a
        // live chat (or test) cannot block the next build of MeepleOS.ServerLayer.
        var temporaryDirectory = Directory.CreateTempSubdirectory("meepleos-mcp-").FullName;
        try
        {
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(server))!;
            foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(temporaryDirectory, Path.GetRelativePath(sourceDirectory, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            var snapshotServer = Path.Combine(temporaryDirectory, Path.GetFileName(server));
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = "dotnet", Arguments = [snapshotServer], WorkingDirectory = root,
                InheritEnvironmentVariables = true,
                EnvironmentVariables = new Dictionary<string, string?> { ["GEMINI_API_KEY"] = null, ["MEEPLEOS_READ_ONLY"] = readOnly ? "1" : "0",
                    ["MEEPLEOS_DB_PATH"] = databasePath ?? Environment.GetEnvironmentVariable("MEEPLEOS_DB_PATH"),
                    ["MEEPLEOS_AUTH_TOKEN"] = authToken ?? Environment.GetEnvironmentVariable("MEEPLEOS_AUTH_TOKEN") }
            });
            return new McpTools(await McpClient.CreateAsync(transport, cancellationToken: ct), readOnly, temporaryDirectory);
        }
        catch
        {
            Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }
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

    public async ValueTask DisposeAsync()
    {
        try { await client.DisposeAsync(); }
        finally
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { Directory.Delete(temporaryServerDirectory, recursive: true); break; }
                catch (IOException) when (attempt < 4) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) when (attempt < 4) { await Task.Delay(100); }
            }
        }
    }
}

using System.Text.Json;

namespace MeepleOS.BusinessLayer.Services;

public sealed record McpAuthPayload(string AccountId, string SecurityStamp)
{
    public string Serialize() => JsonSerializer.Serialize(this);

    public static McpAuthPayload Parse(string value)
    {
        var payload = JsonSerializer.Deserialize<McpAuthPayload>(value);
        if (string.IsNullOrWhiteSpace(payload?.AccountId) || string.IsNullOrWhiteSpace(payload.SecurityStamp))
            throw new UnauthorizedAccessException("Ongeldige MCP-accounttoken.");
        return payload;
    }
}

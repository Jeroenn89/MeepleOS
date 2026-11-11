using System.ComponentModel;
using MeepleOS.BusinessLayer.Services;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer.Tools;

[McpServerToolType]
public sealed class PlayerTools(PlayerService players)
{
    [McpServerTool(Name = "add_player"), Description("Registreer een speler; een bestaande naam levert dezelfde speler op.")]
    public Task<PlayerResult> AddPlayer(string name) => players.AddAsync(name);

    [McpServerTool(Name = "get_players"), Description("Haal alle geregistreerde spelers op.")]
    public Task<List<PlayerResult>> GetPlayers() => players.GetAllAsync();
}

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

    [McpServerTool(Name = "update_player"), Description("Wijzig de naam van een bestaande speler.")]
    public Task<PlayerResult> UpdatePlayer(int id, string name) => players.UpdateAsync(id, name);

    [McpServerTool(Name = "delete_player", Destructive = true),
     Description("VERWIJDER een speler definitief. Vraag vooraf bevestiging. Spelers in gespeelde potjes kunnen niet worden verwijderd.")]
    public Task<bool> DeletePlayer(int id) => players.DeleteAsync(id);
}

using System.ComponentModel;
using MeepleOS.BusinessLayer.Services;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer.Tools;

[McpServerToolType]
public sealed class BoardgameTools(BoardgameService boardgames)
{
    [McpServerTool(Name = "add_boardgame"), Description("Voeg een bordspel toe met spelersaantal en geschatte speelduur in minuten.")]
    public Task<BoardgameResult> AddBoardgame(string name, int minPlayers, int maxPlayers, int estimatedMinutes) =>
        boardgames.AddAsync(name, minPlayers, maxPlayers, estimatedMinutes);

    [McpServerTool(Name = "get_boardgames"), Description("Haal alle opgeslagen bordspellen op.")]
    public Task<List<BoardgameResult>> GetBoardgames() => boardgames.GetAllAsync();

    [McpServerTool(Name = "update_boardgame"), Description("Wijzig opgegeven velden van een bestaand bordspel.")]
    public Task<BoardgameResult> UpdateBoardgame(int id, string? name = null, int? minPlayers = null,
        int? maxPlayers = null, int? estimatedMinutes = null) =>
        boardgames.UpdateAsync(id, name, minPlayers, maxPlayers, estimatedMinutes);

    [McpServerTool(Name = "delete_boardgame", Destructive = true),
     Description("VERWIJDER een bordspel definitief. Vraag vooraf bevestiging. Potjes met dit bordspel blokkeren verwijdering.")]
    public Task<bool> DeleteBoardgame(int id) => boardgames.DeleteAsync(id);
}

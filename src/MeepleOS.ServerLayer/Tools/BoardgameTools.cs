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
}

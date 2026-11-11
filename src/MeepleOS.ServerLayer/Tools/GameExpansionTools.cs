using System.ComponentModel;
using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer.Tools;

[McpServerToolType]
public sealed class GameExpansionTools(GameExpansionService expansions)
{
    [McpServerTool(Name = "add_game_expansion"), Description("Voeg een uitbreiding toe aan een bestaand bordspel.")]
    public Task<GameExpansionResult> AddGameExpansion(int boardGameId, string name, string? description = null) =>
        expansions.AddAsync(boardGameId, name, description);

    [McpServerTool(Name = "get_game_expansions"), Description("Haal uitbreidingen op, eventueel gefilterd op bordspel.")]
    public Task<List<GameExpansionResult>> GetGameExpansions(int? boardGameId = null) =>
        expansions.GetAllAsync(boardGameId);

    [McpServerTool(Name = "update_game_expansion"), Description("Wijzig naam en/of beschrijving via changes; description: null wist de beschrijving.")]
    public Task<GameExpansionResult> UpdateGameExpansion(int id, JsonElement changes) =>
        expansions.UpdateAsync(id, changes);

    [McpServerTool(Name = "delete_game_expansion", Destructive = true),
     Description("VERWIJDER een uitbreiding zonder speelgeschiedenis definitief. Vraag vooraf bevestiging.")]
    public Task<bool> DeleteGameExpansion(int id) => expansions.DeleteAsync(id);
}

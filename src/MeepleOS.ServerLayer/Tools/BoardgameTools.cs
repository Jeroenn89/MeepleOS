using System.ComponentModel;
using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer.Tools;

[McpServerToolType]
public sealed class BoardgameTools(BoardgameService boardgames)
{
    [McpServerTool(Name = "add_boardgame"), Description("Voeg een bordspel toe. maxPlayers is verplicht: geef een getal op, of expliciet null als het spel geen vast maximum heeft (bijvoorbeeld 'onbeperkt aantal spelers' of 'geen maximum'). Vraag om verduidelijking als het maximum onbekend is.")]
    public Task<BoardgameResult> AddBoardgame(string name, int minPlayers, int? maxPlayers, int estimatedMinutes) =>
        boardgames.AddAsync(name, minPlayers, maxPlayers, estimatedMinutes);

    [McpServerTool(Name = "get_boardgames"), Description("Haal alle opgeslagen bordspellen op.")]
    public Task<List<BoardgameResult>> GetBoardgames() => boardgames.GetAllAsync();

    [McpServerTool(Name = "update_boardgame"), Description("Wijzig velden via changes. Laat maxPlayers weg om het te behouden; maxPlayers: null verwijdert een vast maximum wanneer de gebruiker aangeeft dat het spel geen maximum heeft.")]
    public Task<BoardgameResult> UpdateBoardgame(int id, JsonElement changes) =>
        boardgames.UpdateAsync(id, changes);

    [McpServerTool(Name = "delete_boardgame", Destructive = true),
     Description("VERWIJDER een bordspel definitief. Vraag vooraf bevestiging. Potjes met dit bordspel blokkeren verwijdering.")]
    public Task<bool> DeleteBoardgame(int id) => boardgames.DeleteAsync(id);
}

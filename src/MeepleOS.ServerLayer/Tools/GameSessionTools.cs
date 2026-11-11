using System.ComponentModel;
using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer.Tools;

[McpServerToolType]
public sealed class GameSessionTools(GameSessionService sessions)
{
    [McpServerTool(Name = "record_game_session"),
     Description("Registreer een potje met deelnemers, winnaar-ID's en optionele uitbreiding-ID's, scores, eindposities en opmerkingen.")]
    public Task<GameSessionResult> RecordGameSession(int boardGameId, DateOnly playedAt,
        List<SessionPlayerInput> players, List<int> winnerPlayerIds, string? notes = null,
        List<int>? expansionIds = null) =>
        sessions.RecordAsync(boardGameId, playedAt, players, winnerPlayerIds, notes, expansionIds);

    [McpServerTool(Name = "get_game_sessions"), Description("Haal gespeelde potjes op, optioneel gefilterd op bordspel, deelnemer en inclusieve datumperiode.")]
    public Task<List<GameSessionResult>> GetGameSessions(int? boardGameId = null, int? playerId = null,
        DateOnly? from = null, DateOnly? to = null) =>
        sessions.GetAllAsync(boardGameId, playerId, from, to);

    [McpServerTool(Name = "update_game_session"),
     Description("Wijzig een potje via changes. expansionIds vervangt alle uitbreidingen (een lege lijst wist ze); expansionIdsToAdd en expansionIdsToRemove wijzigen de lijst. Weglaten behoudt uitbreidingen. null wist notes, score of position.")]
    public Task<GameSessionResult> UpdateGameSession(int id, JsonElement changes) =>
        sessions.UpdateAsync(id, changes);

    [McpServerTool(Name = "delete_game_session", Destructive = true),
     Description("VERWIJDER een potje definitief, inclusief deelnemers en uitbreidingskoppelingen. De uitbreidingen blijven bestaan. Vraag vooraf bevestiging.")]
    public Task<bool> DeleteGameSession(int id) => sessions.DeleteAsync(id);
}

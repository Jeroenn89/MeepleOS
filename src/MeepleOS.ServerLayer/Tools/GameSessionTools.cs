using System.ComponentModel;
using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer.Tools;

[McpServerToolType]
public sealed class GameSessionTools(GameSessionService sessions)
{
    [McpServerTool(Name = "record_game_session"),
     Description("Registreer een gespeeld bordspelpotje. Geef deelnemers met optionele score en eindpositie op, en één of meer winnaar-ID's.")]
    public Task<GameSessionResult> RecordGameSession(int boardGameId, DateOnly playedAt,
        List<SessionPlayerInput> players, List<int> winnerPlayerIds, string? notes = null) =>
        sessions.RecordAsync(boardGameId, playedAt, players, winnerPlayerIds, notes);

    [McpServerTool(Name = "get_game_sessions"), Description("Haal gespeelde potjes op, optioneel gefilterd op bordspel, deelnemer en inclusieve datumperiode.")]
    public Task<List<GameSessionResult>> GetGameSessions(int? boardGameId = null, int? playerId = null,
        DateOnly? from = null, DateOnly? to = null) =>
        sessions.GetAllAsync(boardGameId, playerId, from, to);

    [McpServerTool(Name = "update_game_session"),
     Description("Wijzig een potje. Geef alleen te wijzigen velden in changes op; null wist notes, score of position. Gebruik players om deelnemers te vervangen of playerChanges om scores en posities per deelnemer te wijzigen.")]
    public Task<GameSessionResult> UpdateGameSession(int id, JsonElement changes) =>
        sessions.UpdateAsync(id, changes);

    [McpServerTool(Name = "delete_game_session", Destructive = true),
     Description("VERWIJDER een gespeeld potje definitief, inclusief deelnameregistraties. Vraag vooraf bevestiging.")]
    public Task<bool> DeleteGameSession(int id) => sessions.DeleteAsync(id);
}

using System.ComponentModel;
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
}

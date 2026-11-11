using System.ComponentModel;
using MeepleOS.BusinessLayer.Services;
using ModelContextProtocol.Server;

namespace MeepleOS.ServerLayer.Tools;

[McpServerToolType]
public sealed class StatisticsTools(StatisticsService statistics)
{
    [McpServerTool(Name = "get_player_statistics"),
     Description("Potjes, overwinningen en winstpercentage per speler. Filter optioneel op playerId, boardGameId of een ISO-week, maand, jaar of inclusieve datumperiode.")]
    public Task<List<PlayerStatistics>> GetPlayerStatistics(int? playerId = null, int? boardGameId = null,
        int? year = null, int? month = null, int? week = null, DateOnly? from = null, DateOnly? to = null) =>
        statistics.GetPlayerStatisticsAsync(playerId, boardGameId, StatisticsPeriod.Create(year, month, week, from, to));

    [McpServerTool(Name = "get_boardgame_statistics"),
     Description("Potjes, unieke spelers en spelers met de meeste overwinningen per bordspel. Filter optioneel op boardGameId of een ISO-week, maand, jaar of inclusieve datumperiode.")]
    public Task<List<BoardgameStatistics>> GetBoardgameStatistics(int? boardGameId = null,
        int? year = null, int? month = null, int? week = null, DateOnly? from = null, DateOnly? to = null) =>
        statistics.GetBoardgameStatisticsAsync(boardGameId, StatisticsPeriod.Create(year, month, week, from, to));

    [McpServerTool(Name = "get_leaderboard"),
     Description("Ranglijst van spelers op wins of winPercentage, met gespeelde potjes. Filter optioneel op boardGameId of een ISO-week, maand, jaar of inclusieve datumperiode.")]
    public Task<List<LeaderboardEntry>> GetLeaderboard(string sortBy = "wins", int? boardGameId = null,
        int? year = null, int? month = null, int? week = null, DateOnly? from = null, DateOnly? to = null) =>
        statistics.GetLeaderboardAsync(sortBy, boardGameId, StatisticsPeriod.Create(year, month, week, from, to));

    [McpServerTool(Name = "get_play_statistics"),
     Description("Totaal potjes, potjes per week/maand/jaar en meest gespeelde bordspellen. Kies groupBy en filter optioneel op een ISO-week, maand, jaar of inclusieve datumperiode.")]
    public Task<PlayStatistics> GetPlayStatistics(string groupBy = "month",
        int? year = null, int? month = null, int? week = null, DateOnly? from = null, DateOnly? to = null) =>
        statistics.GetPlayStatisticsAsync(groupBy, StatisticsPeriod.Create(year, month, week, from, to));
}

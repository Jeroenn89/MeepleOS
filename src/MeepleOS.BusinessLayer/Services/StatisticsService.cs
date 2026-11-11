using System.Globalization;
using MeepleOS.BusinessLayer.Repositories;

namespace MeepleOS.BusinessLayer.Services;

public sealed record PlayerStatistics(int PlayerId, string PlayerName, int Plays, int Wins, double WinPercentage);

public sealed record BoardgameWinner(int PlayerId, string PlayerName, int Wins);

public sealed record BoardgameStatistics(int BoardGameId, string BoardGameName, int Plays,
    int UniquePlayers, List<BoardgameWinner> TopWinners);

public sealed record LeaderboardEntry(int Rank, int PlayerId, string PlayerName, int Plays,
    int Wins, double WinPercentage);

public sealed record PlayCount(string Period, int Plays);

public sealed record PopularBoardgame(int BoardGameId, string BoardGameName, int Plays);

public sealed record PlayStatistics(int TotalPlays, List<PlayCount> PlaysByPeriod,
    List<PopularBoardgame> MostPlayedBoardgames);

public sealed class StatisticsService(IGameSessionRepository sessions, IPlayerRepository players,
    IBoardgameRepository boardgames)
{
    public async Task<List<PlayerStatistics>> GetPlayerStatisticsAsync(int? playerId = null,
        int? boardGameId = null, StatisticsPeriod? period = null)
    {
        ValidateId(playerId, nameof(playerId));
        ValidateId(boardGameId, nameof(boardGameId));
        period ??= new(null, null);

        var allPlayers = await players.GetAllAsync();
        var history = await sessions.GetAllAsync(boardGameId, playerId, period.From, period.To);

        return allPlayers
            .Where(p => !playerId.HasValue || p.Id == playerId.Value)
            .Select(p =>
            {
                var appearances = history
                    .SelectMany(s => s.Players)
                    .Where(x => x.PlayerId == p.Id)
                    .ToList();
                var wins = appearances.Count(x => x.IsWinner);

                return new PlayerStatistics(p.Id, p.Name, appearances.Count, wins,
                    Percentage(wins, appearances.Count));
            })
            .OrderBy(x => x.PlayerName)
            .ThenBy(x => x.PlayerId)
            .ToList();
    }

    public async Task<List<BoardgameStatistics>> GetBoardgameStatisticsAsync(int? boardGameId = null,
        StatisticsPeriod? period = null)
    {
        ValidateId(boardGameId, nameof(boardGameId));
        period ??= new(null, null);

        var allGames = await boardgames.GetAllAsync();
        var history = await sessions.GetAllAsync(boardGameId, null, period.From, period.To);

        return allGames
            .Where(g => !boardGameId.HasValue || g.Id == boardGameId.Value)
            .Select(g =>
            {
                var gameSessions = history.Where(s => s.BoardGameId == g.Id).ToList();
                var participants = gameSessions.SelectMany(s => s.Players).ToList();
                var winnerCounts = participants
                    .Where(p => p.IsWinner)
                    .GroupBy(p => new { p.PlayerId, p.PlayerName })
                    .Select(group => new BoardgameWinner(group.Key.PlayerId, group.Key.PlayerName, group.Count()))
                    .ToList();
                var mostWins = winnerCounts.Count == 0 ? 0 : winnerCounts.Max(x => x.Wins);
                var topWinners = winnerCounts
                    .Where(x => x.Wins == mostWins)
                    .OrderBy(x => x.PlayerName)
                    .ThenBy(x => x.PlayerId)
                    .ToList();

                return new BoardgameStatistics(g.Id, g.Name, gameSessions.Count,
                    participants.Select(p => p.PlayerId).Distinct().Count(), topWinners);
            })
            .OrderBy(x => x.BoardGameName)
            .ThenBy(x => x.BoardGameId)
            .ToList();
    }

    public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(string sortBy = "wins",
        int? boardGameId = null, StatisticsPeriod? period = null)
    {
        if (sortBy is not ("wins" or "winPercentage"))
            throw new ArgumentException("Sorteer op 'wins' of 'winPercentage'.", nameof(sortBy));

        var statistics = await GetPlayerStatisticsAsync(boardGameId: boardGameId, period: period);
        var sorted = sortBy == "wins"
            ? statistics.OrderByDescending(x => x.Wins).ThenByDescending(x => x.WinPercentage)
            : statistics.OrderByDescending(x => x.WinPercentage).ThenByDescending(x => x.Wins);

        return sorted
            .ThenByDescending(x => x.Plays)
            .ThenBy(x => x.PlayerName)
            .ThenBy(x => x.PlayerId)
            .Select((x, index) => new LeaderboardEntry(index + 1, x.PlayerId, x.PlayerName,
                x.Plays, x.Wins, x.WinPercentage))
            .ToList();
    }

    public async Task<PlayStatistics> GetPlayStatisticsAsync(string groupBy = "month",
        StatisticsPeriod? period = null)
    {
        if (groupBy is not ("week" or "month" or "year"))
            throw new ArgumentException("Groepeer per 'week', 'month' of 'year'.", nameof(groupBy));

        period ??= new(null, null);
        var history = await sessions.GetAllAsync(null, null, period.From, period.To);
        var grouped = history
            .GroupBy(s => PeriodKey(s.PlayedAt, groupBy))
            .OrderBy(g => g.Key)
            .Select(g => new PlayCount(g.Key, g.Count()))
            .ToList();
        var popular = history
            .GroupBy(s => new { s.BoardGameId, s.BoardGameName })
            .Select(g => new PopularBoardgame(g.Key.BoardGameId, g.Key.BoardGameName, g.Count()))
            .OrderByDescending(x => x.Plays)
            .ThenBy(x => x.BoardGameName)
            .ThenBy(x => x.BoardGameId)
            .ToList();

        return new(history.Count, grouped, popular);
    }

    private static double Percentage(int wins, int plays) => plays == 0 ? 0 : wins * 100.0 / plays;

    private static void ValidateId(int? id, string name)
    {
        if (id is < 1)
            throw new ArgumentException("Geef een geldig ID op.", name);
    }

    private static string PeriodKey(DateOnly date, string groupBy) => groupBy switch
    {
        "week" => $"{ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue)):D4}-W{ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)):D2}",
        "month" => $"{date.Year:D4}-{date.Month:D2}",
        _ => date.Year.ToString("D4", CultureInfo.InvariantCulture)
    };
}

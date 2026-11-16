using System.Globalization;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;

namespace MeepleOS.Web.Components.Pages;

public partial class Statistics
{
    private List<BoardgameResult> games = [];
    private Dictionary<int, int> minutesByGame = [];
    private List<PlayerResult> players = [];
    private FilteredStatistics? filtered;
    private List<ChartItem> timeBars = [];
    private List<ChartItem> gameBars = [];
    private List<ChartItem> winBars = [];
    private List<ChartItem> playBars = [];
    private List<ChartItem> weekdayBars = [];
    private List<LineChartPoint> linePoints = [];
    private int chartVersion;
    private List<GameSessionResult> selectedSessions = [];
    private List<GameSessionResult> recentSessions = [];
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    private string period = "all";
    private string groupBy = "month";
    private int year = DateTime.Now.Year;
    private int month = DateTime.Now.Month;
    private int week = ISOWeek.GetWeekOfYear(DateTime.Now);
    private int gameId;
    private int playerId;
    private DateTime? from;
    private DateTime? to;
    private string? error;
    private string periodDescription = "Alle tijd";
    private string rankingSort = "wins";

    private string PeriodDescription => periodDescription;
    private int DistinctGames => selectedSessions.Select(session => session.BoardGameId).Distinct().Count();
    private int ActivePlayers => selectedSessions.SelectMany(session => session.Players).Select(player => player.PlayerId).Distinct().Count();
    private double AveragePlayers => selectedSessions.Count == 0 ? 0 : selectedSessions.Average(session => session.Players.Count);
    private double EstimatedHours => selectedSessions.Sum(session => (long)minutesByGame.GetValueOrDefault(session.BoardGameId)) / 60.0;
    private PopularBoardgame? TopGame => filtered?.MostPlayedBoardgames.FirstOrDefault();
    private PlayerStatistics? TopWinner => filtered?.Players.FirstOrDefault();
    private IOrderedEnumerable<PlayerStatistics> RankedPlayers => rankingSort switch
    {
        "percentage" => filtered!.Players.OrderByDescending(player => player.WinPercentage).ThenByDescending(player => player.Wins).ThenBy(player => player.PlayerName),
        "plays" => filtered!.Players.OrderByDescending(player => player.Plays).ThenByDescending(player => player.Wins).ThenBy(player => player.PlayerName),
        _ => filtered!.Players.OrderByDescending(player => player.Wins).ThenByDescending(player => player.WinPercentage).ThenBy(player => player.PlayerName)
    };
    private IGrouping<DayOfWeek, GameSessionResult>? BusiestDay => selectedSessions
        .GroupBy(session => session.PlayedAt.DayOfWeek)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => ((int)group.Key + 6) % 7)
        .FirstOrDefault();
    private string BusiestDayLabel => BusiestDay is { } day ? Dutch.DateTimeFormat.GetDayName(day.Key) : "—";
    private int BusiestDayCount => BusiestDay?.Count() ?? 0;

    protected override async Task OnInitializedAsync()
    {
        games = await Games.GetAllAsync();
        minutesByGame = games.ToDictionary(game => game.Id, game => game.EstimatedMinutes);
        players = await Players.GetAllAsync();
        await Apply();
    }

    private async Task Apply()
    {
        try
        {
            error = null;
            var selectedPeriod = period switch
            {
                "week" => StatisticsPeriod.Create(year: year, week: week),
                "month" => StatisticsPeriod.Create(year: year, month: month),
                "year" => StatisticsPeriod.Create(year: year),
                "custom" => StatisticsPeriod.Create(
                    from: from.HasValue ? DateOnly.FromDateTime(from.Value) : null,
                    to: to.HasValue ? DateOnly.FromDateTime(to.Value) : null),
                _ => StatisticsPeriod.Create()
            };

            filtered = await Stats.GetFilteredStatisticsAsync(
                playerId == 0 ? null : playerId,
                gameId == 0 ? null : gameId,
                groupBy,
                selectedPeriod);

            selectedSessions = await Sessions.GetAllAsync(
                gameId == 0 ? null : gameId,
                playerId == 0 ? null : playerId,
                selectedPeriod.From,
                selectedPeriod.To);
            linePoints = selectedSessions.GroupBy(session => session.PlayedAt)
                .OrderBy(group => group.Key)
                .Select(group => new LineChartPoint(group.Key, group.Count())).ToList();
            chartVersion++;
            recentSessions = selectedSessions.OrderByDescending(session => session.PlayedAt)
                .ThenByDescending(session => session.Id).Take(5).ToList();
            periodDescription = selectedPeriod.From is null && selectedPeriod.To is null
                ? "Alle tijd"
                : $"{selectedPeriod.From?.ToString("dd MMM yyyy", Dutch) ?? "Begin"} – {selectedPeriod.To?.ToString("dd MMM yyyy", Dutch) ?? "nu"}";

            timeBars = filtered.PlaysByPeriod
                .Select(count => new ChartItem(count.Period, count.Plays))
                .ToList();
            gameBars = filtered.MostPlayedBoardgames
                .Take(10)
                .Select(game => new ChartItem(game.BoardGameName, game.Plays))
                .ToList();
            winBars = filtered.Players
                .Where(player => player.Wins > 0)
                .Take(10)
                .Select(player => new ChartItem(player.PlayerName, player.Wins))
                .ToList();
            playBars = filtered.Players.OrderByDescending(player => player.Plays)
                .ThenBy(player => player.PlayerName).Take(10)
                .Select(player => new ChartItem(player.PlayerName, player.Plays)).ToList();
            weekdayBars = Enumerable.Range(0, 7)
                .Select(index => (DayOfWeek)((index + 1) % 7))
                .Select(day => new ChartItem(Dutch.DateTimeFormat.GetDayName(day),
                    selectedSessions.Count(session => session.PlayedAt.DayOfWeek == day)))
                .ToList();
        }
        catch (Exception exception)
        {
            error = exception.Message;
            filtered = null;
        }
    }

    private async Task ResetFilters()
    {
        period = "all";
        groupBy = "month";
        year = DateTime.Now.Year;
        month = DateTime.Now.Month;
        week = ISOWeek.GetWeekOfYear(DateTime.Now);
        gameId = 0;
        playerId = 0;
        from = null;
        to = null;
        await Apply();
    }
}

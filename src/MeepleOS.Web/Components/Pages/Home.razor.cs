using System.Globalization;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;

namespace MeepleOS.Web.Components.Pages;

public partial class Home
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    private List<GameSessionResult>? history;
    private int playerCount;
    private int gameCount;
    private long estimatedMinutes;
    private List<LineChartPoint> playPoints = [];
    private int[] years = [];
    private static readonly int[] months = Enumerable.Range(1, 12).ToArray();
    private string gamePeriod = "all";
    private string winPeriod = "all";
    private int gameYear;
    private int winYear;
    private int monthChartYear;
    private int gameMonth;
    private int winMonth;
    private DateTime? gameFrom;
    private DateTime? gameTo;
    private DateTime? winFrom;
    private DateTime? winTo;

    protected override async Task OnInitializedAsync()
    {
        var games = await Games.GetAllAsync();
        var players = await Players.GetAllAsync();
        history = await Sessions.GetAllAsync();
        gameCount = games.Count;
        playerCount = players.Count;
        var minutesByGame = games.ToDictionary(game => game.Id, game => game.EstimatedMinutes);
        estimatedMinutes = history.Sum(session => (long)minutesByGame.GetValueOrDefault(session.BoardGameId));

        playPoints = history.GroupBy(session => session.PlayedAt)
            .OrderBy(group => group.Key)
            .Select(group => new LineChartPoint(group.Key, group.Count())).ToList();
        years = history.Select(session => session.PlayedAt.Year).Distinct().OrderDescending().ToArray();
        var latest = history.Count == 0 ? DateOnly.FromDateTime(DateTime.Today) : history.Max(session => session.PlayedAt);
        gameYear = winYear = monthChartYear = latest.Year;
        gameMonth = winMonth = latest.Month;
    }

    private List<ChartItem> MonthBars => (history ?? [])
        .Where(session => session.PlayedAt.Year == monthChartYear)
        .GroupBy(session => session.PlayedAt.Month)
        .OrderBy(group => group.Key)
        .Select(group => new ChartItem($"{monthChartYear:D4}-{group.Key:D2}", group.Count()))
        .ToList();

    private List<ChartItem> GameBars => Filter(gamePeriod, gameYear, gameMonth, gameFrom, gameTo)
        .GroupBy(session => new { session.BoardGameId, session.BoardGameName })
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key.BoardGameName)
        .Take(8)
        .Select(group => new ChartItem(group.Key.BoardGameName, group.Count())).ToList();

    private List<ChartItem> WinBars => Filter(winPeriod, winYear, winMonth, winFrom, winTo)
        .SelectMany(session => session.Players)
        .Where(player => player.IsWinner)
        .GroupBy(player => new { player.PlayerId, player.PlayerName })
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key.PlayerName)
        .Take(8)
        .Select(group => new ChartItem(group.Key.PlayerName, group.Count())).ToList();

    private IEnumerable<GameSessionResult> Filter(string period, int year, int month, DateTime? from, DateTime? to)
    {
        var sessions = history ?? [];
        return period switch
        {
            "year" => sessions.Where(session => session.PlayedAt.Year == year),
            "month" => sessions.Where(session => session.PlayedAt.Year == year && session.PlayedAt.Month == month),
            "custom" => sessions.Where(session => (!from.HasValue || session.PlayedAt >= DateOnly.FromDateTime(from.Value))
                && (!to.HasValue || session.PlayedAt <= DateOnly.FromDateTime(to.Value))),
            _ => sessions
        };
    }

    private static string MonthName(int month) => Dutch.DateTimeFormat.GetMonthName(month);
}

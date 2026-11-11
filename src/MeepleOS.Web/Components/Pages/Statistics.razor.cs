using System.Globalization;
using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;

namespace MeepleOS.Web.Components.Pages;

public partial class Statistics
{
    private List<BoardgameResult> games = [];
    private List<PlayerResult> players = [];
    private FilteredStatistics? filtered;
    private List<ChartItem> timeBars = [];
    private List<ChartItem> gameBars = [];
    private List<ChartItem> winBars = [];

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

    protected override async Task OnInitializedAsync()
    {
        games = await Games.GetAllAsync();
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

            timeBars = filtered.PlaysByPeriod
                .Select(count => new ChartItem(count.Period, count.Plays))
                .ToList();
            gameBars = filtered.MostPlayedBoardgames
                .Select(game => new ChartItem(game.BoardGameName, game.Plays))
                .ToList();
            winBars = filtered.Players
                .Where(player => player.Wins > 0)
                .Select(player => new ChartItem(player.PlayerName, player.Wins))
                .ToList();
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
    }
}

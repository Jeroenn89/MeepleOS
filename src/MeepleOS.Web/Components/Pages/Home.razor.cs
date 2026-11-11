using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;

namespace MeepleOS.Web.Components.Pages;

public partial class Home
{
    private List<GameSessionResult>? history;
    private int playerCount;
    private int gameCount;
    private int wins;
    private List<ChartItem> timeBars = [];
    private List<ChartItem> gameBars = [];
    private List<ChartItem> winBars = [];

    protected override async Task OnInitializedAsync()
    {
        var games = await Games.GetAllAsync();
        var players = await Players.GetAllAsync();
        history = await Sessions.GetAllAsync();

        gameCount = games.Count;
        playerCount = players.Count;
        wins = history.Sum(session => session.Players.Count(player => player.IsWinner));

        timeBars = history
            .GroupBy(session => session.PlayedAt.ToString("yyyy-MM"))
            .OrderBy(group => group.Key)
            .TakeLast(12)
            .Select(group => new ChartItem(group.Key, group.Count()))
            .ToList();

        gameBars = history
            .GroupBy(session => new { session.BoardGameId, session.BoardGameName })
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.BoardGameName)
            .Take(8)
            .Select(group => new ChartItem(group.Key.BoardGameName, group.Count()))
            .ToList();

        winBars = history
            .SelectMany(session => session.Players)
            .Where(player => player.IsWinner)
            .GroupBy(player => player.PlayerId)
            .Select(group => new
            {
                Name = players.FirstOrDefault(player => player.Id == group.Key)?.Name
                    ?? group.First().PlayerName,
                Count = group.Count()
            })
            .OrderByDescending(result => result.Count)
            .ThenBy(result => result.Name)
            .Take(8)
            .Select(result => new ChartItem(result.Name, result.Count))
            .ToList();
    }
}

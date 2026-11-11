using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components.Pages;

public partial class PlayerProfile
{
    [Parameter] public int Id { get; set; }

    private PlayerResult? player;
    private List<GameSessionResult> history = [];
    private List<ChartItem> gameBars = [];
    private int wins;
    private double percentage;

    protected override async Task OnParametersSetAsync()
    {
        player = (await Players.GetAllAsync()).FirstOrDefault(player => player.Id == Id);
        if (player is null)
            return;

        history = await Sessions.GetAllAsync(playerId: Id);
        var statistics = (await Stats.GetPlayerStatisticsAsync(playerId: Id)).Single();
        wins = statistics.Wins;
        percentage = statistics.WinPercentage;

        gameBars = history
            .GroupBy(session => session.BoardGameName)
            .OrderByDescending(group => group.Count())
            .Take(10)
            .Select(group => new ChartItem(group.Key, group.Count()))
            .ToList();
    }
}

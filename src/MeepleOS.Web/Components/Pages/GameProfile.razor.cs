using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components.Pages;

public partial class GameProfile
{
    [Parameter] public int Id { get; set; }

    private BoardgameResult? game;
    private List<GameSessionResult> history = [];
    private List<GameExpansionResult> expansions = [];
    private List<PlayerStatistics> percentages = [];
    private List<ChartItem> trend = [];
    private List<ChartItem> participants = [];
    private List<ChartItem> winners = [];

    protected override async Task OnParametersSetAsync()
    {
        game = (await Games.GetAllAsync()).FirstOrDefault(game => game.Id == Id);
        if (game is null)
            return;

        history = await Sessions.GetAllAsync(boardGameId: Id);
        expansions = await Expansions.GetAllAsync(Id);
        percentages = (await Stats.GetPlayerStatisticsAsync(boardGameId: Id))
            .Where(player => player.Plays > 0)
            .OrderByDescending(player => player.Wins)
            .ToList();

        trend = history
            .GroupBy(session => session.PlayedAt.ToString("yyyy-MM"))
            .OrderBy(group => group.Key)
            .Select(group => new ChartItem(group.Key, group.Count()))
            .ToList();
        participants = percentages
            .OrderByDescending(player => player.Plays)
            .Take(10)
            .Select(player => new ChartItem(player.PlayerName, player.Plays))
            .ToList();
        winners = percentages
            .Where(player => player.Wins > 0)
            .Take(10)
            .Select(player => new ChartItem(player.PlayerName, player.Wins))
            .ToList();
    }
}

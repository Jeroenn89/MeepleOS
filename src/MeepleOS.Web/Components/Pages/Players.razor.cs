using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components.Pages;

public partial class Players
{
    private List<PlayerResult>? players;
    private List<PlayerStatistics>? stats;
    private string? search;

    protected override async Task OnInitializedAsync()
    {
        players = await PlayerService.GetAllAsync();
        stats = await Stats.GetPlayerStatisticsAsync();
    }

    private bool MatchesSearch(PlayerResult player) =>
        player.Name.Contains(search ?? "", StringComparison.OrdinalIgnoreCase);
}

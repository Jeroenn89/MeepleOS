using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components.Pages;

public partial class Games
{
    private List<BoardgameResult>? games;
    private List<GameExpansionResult>? expansions;
    private List<GameSessionResult>? sessions;
    private string? search;
    private int? players;
    private int? minutes;

    protected override async Task OnInitializedAsync()
    {
        games = await GameService.GetAllAsync();
        expansions = await ExpansionService.GetAllAsync();
        sessions = await SessionService.GetAllAsync();
    }

    private bool MatchesFilters(BoardgameResult game) =>
        game.Name.Contains(search ?? "", StringComparison.OrdinalIgnoreCase)
        && (!players.HasValue ||
            game.MinPlayers <= players.Value &&
            (!game.MaxPlayers.HasValue || game.MaxPlayers >= players.Value))
        && (!minutes.HasValue || game.EstimatedMinutes <= minutes.Value);
}

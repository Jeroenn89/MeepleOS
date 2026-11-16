using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.Web.Components.Pages;

public partial class Games
{
    private List<BoardgameResult>? games;
    private Dictionary<int, int> playCounts = [];
    private string? search;
    private int? players;
    private int? minutes;

    protected override async Task OnInitializedAsync()
    {
        games = await GameService.GetAllAsync();
        playCounts = (await SessionService.GetAllAsync())
            .GroupBy(session => session.BoardGameId)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    private bool MatchesFilters(BoardgameResult game) =>
        game.Name.Contains(search ?? "", StringComparison.OrdinalIgnoreCase)
        && (!players.HasValue ||
            game.MinPlayers <= players.Value &&
            (!game.MaxPlayers.HasValue || game.MaxPlayers >= players.Value))
        && (!minutes.HasValue || game.EstimatedMinutes <= minutes.Value);
}

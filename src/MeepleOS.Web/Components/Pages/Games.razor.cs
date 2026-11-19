using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.Web.Components.Pages;

public partial class Games
{
    private List<BoardgameResult>? games;
    private Dictionary<int, int> playCounts = [];
    private Dictionary<int, string> cardDecorations = [];
    private static readonly string[] DecorationStyles =
    [
        "game-card-fold", "game-card-stain", "game-card-tape",
        "game-card-stamp", "game-card-scribble"
    ];
    private string? search;
    private int? players;
    private int? minutes;
    private string viewMode = "grid";

    private void ShowGrid() => viewMode = "grid";
    private void ShowNames() => viewMode = "names";

    protected override async Task OnInitializedAsync()
    {
        games = await GameService.GetAllAsync();
        cardDecorations = games.ToDictionary(game => game.Id,
            _ => Random.Shared.Next(5) == 0
                ? DecorationStyles[Random.Shared.Next(DecorationStyles.Length)]
                : "");
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

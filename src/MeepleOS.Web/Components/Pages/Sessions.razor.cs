using MeepleOS.BusinessLayer.Services;
using System.Globalization;

namespace MeepleOS.Web.Components.Pages;

public partial class Sessions
{
    private List<GameSessionResult>? history;
    private List<BoardgameResult> games = [];
    private List<PlayerResult> players = [];
    private int gameId;
    private int playerId;
    private static readonly CultureInfo DutchCulture = CultureInfo.GetCultureInfo("nl-NL");

    private List<GameSessionResult> VisibleSessions => history?
        .Where(MatchesFilters)
        .OrderByDescending(session => session.PlayedAt)
        .ThenByDescending(session => session.Id)
        .ToList() ?? [];

    protected override async Task OnInitializedAsync()
    {
        games = await GameService.GetAllAsync();
        players = await PlayerService.GetAllAsync();
        history = await SessionService.GetAllAsync();
    }

    private bool MatchesFilters(GameSessionResult session) =>
        (gameId == 0 || session.BoardGameId == gameId)
        && (playerId == 0 || session.Players.Any(player => player.PlayerId == playerId));
}

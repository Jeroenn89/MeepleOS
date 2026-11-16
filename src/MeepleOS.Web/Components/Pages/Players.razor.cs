using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.Web.Components.Pages;

public partial class Players
{
    private List<PlayerResult>? players;
    private Dictionary<int, PlayerStatistics> statsByPlayer = [];
    private Dictionary<int, int> playRanks = [];
    private Dictionary<int, int> winRateRanks = [];
    private Dictionary<int, int> winRanks = [];
    private string? search;
    private string sortBy = "none";

    protected override async Task OnInitializedAsync()
    {
        players = await PlayerService.GetAllAsync();
        statsByPlayer = (await Stats.GetPlayerStatisticsAsync())
            .ToDictionary(statistic => statistic.PlayerId);
        var statistics = statsByPlayer.Values;
        playRanks = TopThreeRanks(statistics, statistic => statistic.Plays);
        winRateRanks = TopThreeRanks(statistics, statistic => statistic.WinPercentage);
        winRanks = TopThreeRanks(statistics, statistic => statistic.Wins);
    }

    private static Dictionary<int, int> TopThreeRanks(IEnumerable<PlayerStatistics> statistics,
        Func<PlayerStatistics, double> value)
    {
        var ranks = new Dictionary<int, int>();
        double? previous = null;
        var position = 0;
        var rank = 0;
        foreach (var statistic in statistics.Where(statistic => value(statistic) > 0)
                     .OrderByDescending(value))
        {
            position++;
            var current = value(statistic);
            if (previous != current) rank = position;
            if (rank > 3) break;
            ranks[statistic.PlayerId] = rank;
            previous = current;
        }
        return ranks;
    }

    private IEnumerable<PlayerResult> VisiblePlayers
    {
        get
        {
            var matching = (players ?? []).Where(MatchesSearch);
            return sortBy switch
            {
                "wins" => matching.OrderByDescending(player => statsByPlayer[player.Id].Wins)
                    .ThenBy(player => player.Name),
                "winPercentage" => matching.OrderByDescending(player => statsByPlayer[player.Id].WinPercentage)
                    .ThenByDescending(player => statsByPlayer[player.Id].Wins)
                    .ThenBy(player => player.Name),
                "plays" => matching.OrderByDescending(player => statsByPlayer[player.Id].Plays)
                    .ThenBy(player => player.Name),
                _ => matching
            };
        }
    }

    private bool MatchesSearch(PlayerResult player) =>
        player.Name.Contains(search ?? "", StringComparison.OrdinalIgnoreCase);
}

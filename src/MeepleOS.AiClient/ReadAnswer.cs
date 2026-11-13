using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace MeepleOS.AiClient;

internal static class ReadAnswer
{
    private static readonly Regex CountQuestion = new(@"\b(?:hoeveel potjes|hoe vaak|aantal potjes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WinQuestion = new(@"\b(?:vaakst gewonnen|meeste overwinningen|meeste keren gewonnen|meeste winst)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlaysQuestion = new(@"\b(?:meeste\s+(?:gespeelde\s+)?potjes|vaakst\s+gespeeld|meest(?:e)?\s+gespeelde?|vaakst\s+op\s+tafel)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OwnQuestion = new(@"\b(?:ik|mij|mezelf|mijn)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CollectionQuestion = new(@"\b(?:collectie|verzameling|hebben|bezit)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CollectionCountQuestion = new(@"\b(?:hoeveel|aantal)\s+(?:verschillende\s+)?(?:bord)?spellen\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WinPercentageQuestion = new(@"\b(?:winpercentage|winstpercentage|winstkans|percentage\s+(?:gewonnen|overwinningen))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<string> AnswerAsync(string question, IReadOnlyList<ActionPlan> actions,
        Catalog catalog, IMeepleTools tools, CancellationToken ct)
    {
        var game = FindGame(question, catalog);
        if (CollectionQuestion.IsMatch(question) && CollectionCountQuestion.IsMatch(question))
            return $"Er staan {catalog.Boardgames.Count} {(catalog.Boardgames.Count == 1 ? "bordspel" : "bordspellen")} in je collectie.";
        if (CollectionQuestion.IsMatch(question) && Regex.IsMatch(question,
            @"^\s*(?:staat|heb\s+ik|bezit\s+ik|is\s+.+\s+aanwezig)\b", RegexOptions.IgnoreCase))
            return game is null ? "Nee, dit bordspel staat niet in je collectie." : $"Ja, {game.Name} staat in je collectie.";

        if (WinPercentageQuestion.IsMatch(question))
        {
            var player = FindPlayer(question, catalog);
            if (player is null && OwnQuestion.IsMatch(question))
                player = catalog.Players.FirstOrDefault(item => item.Name.Equals("Jeroen", StringComparison.OrdinalIgnoreCase));
            var stats = await tools.CallAsync("get_player_statistics", new { playerId = player?.Id, boardGameId = game?.Id }, ct);
            var rows = stats.EnumerateArray().Where(row => player is null || row.GetProperty("playerId").GetInt32() == player.Id).ToArray();
            if (rows.Length == 0) return player is null ? "Er zijn geen spelerstatistieken gevonden." : $"Voor {player.Name} zijn geen spelerstatistieken gevonden.";
            return string.Join(" ", rows.Select(row =>
            {
                var plays = row.GetProperty("plays").GetInt32();
                var wins = row.GetProperty("wins").GetInt32();
                var percentage = plays == 0 ? 0 : 100.0 * wins / plays;
                return $"Het winpercentage van {row.GetProperty("playerName").GetString()} is {percentage.ToString("0.#", CultureInfo.GetCultureInfo("nl-NL"))}% ({wins} overwinningen uit {plays} potjes).";
            }));
        }

        var asksCount = CountQuestion.IsMatch(question);
        var asksWins = WinQuestion.IsMatch(question);
        var asksPlays = PlaysQuestion.IsMatch(question);
        var asksPlayers = Regex.IsMatch(question, @"\bspelers?\b", RegexOptions.IgnoreCase);
        var asksGames = Regex.IsMatch(question, @"\b(?:bordspellen|bordspel|spellen|spel|games)\b", RegexOptions.IgnoreCase);
        if (asksCount || asksWins || asksPlays)
        {
            var own = OwnQuestion.IsMatch(Regex.Replace(question, @"\bmijn\s+(?:collectie|verzameling)\b", "", RegexOptions.IgnoreCase));
            var jeroen = catalog.Players.FirstOrDefault(player => player.Name.Equals("Jeroen", StringComparison.OrdinalIgnoreCase));
            var data = await tools.CallAsync("get_game_sessions", game is null ? null : new { boardGameId = game.Id }, ct);
            var sessions = data.EnumerateArray().Select(row => new Session(
                row.GetProperty("boardGameId").GetInt32(),
                row.GetProperty("players").EnumerateArray().Select(player => new Appearance(
                    player.GetProperty("playerId").GetInt32(),
                    player.TryGetProperty("isWinner", out var winner) && winner.ValueKind == JsonValueKind.True)).ToArray())).ToArray();
            var parts = new List<string>();
            if (asksCount)
            {
                var count = sessions.Count(session => !own || jeroen is not null && session.Players.Any(player => player.PlayerId == jeroen.Id));
                parts.Add(own
                    ? $"Je hebt {count} {(count == 1 ? "potje" : "potjes")}{(game is null ? "" : " " + game.Name)} gespeeld."
                    : $"Er {(count == 1 ? "is" : "zijn")} {count} {(count == 1 ? "potje" : "potjes")}{(game is null ? "" : " " + game.Name)} gespeeld.");
            }
            if (asksPlays)
            {
                var limit = Limit(question, defaultLimit: 1);
                if (asksPlayers && !asksGames)
                {
                    var ranking = sessions.SelectMany(session => session.Players).GroupBy(player => player.PlayerId)
                        .Select(group => (Id: group.Key, Plays: group.Count()))
                        .OrderByDescending(row => row.Plays).ThenBy(row => PlayerName(catalog, row.Id)).ToArray();
                    parts.Add(Ranking("Spelers met de meeste gespeelde potjes", ranking,
                        row => $"{PlayerName(catalog, row.Id)} ({row.Plays} {(row.Plays == 1 ? "potje" : "potjes")})", limit));
                }
                else
                {
                    var ranking = sessions.Where(session => !own || jeroen is not null && session.Players.Any(player => player.PlayerId == jeroen.Id))
                        .GroupBy(session => session.BoardGameId)
                        .Select(group => (Id: group.Key, Plays: group.Count()))
                        .OrderByDescending(row => row.Plays).ThenBy(row => GameName(catalog, row.Id)).ToArray();
                    parts.Add(Ranking(own ? "Je meest gespeelde bordspellen (persoonlijke potjes)" : "Meest gespeelde bordspellen (alle geregistreerde potjes)", ranking,
                        row => $"{GameName(catalog, row.Id)} ({row.Plays} {(row.Plays == 1 ? "potje" : "potjes")})", limit));
                }
            }
            if (asksWins)
            {
                var ranking = sessions.SelectMany(session => session.Players).Where(player => player.IsWinner)
                    .GroupBy(player => player.PlayerId).Select(group => (Id: group.Key, Plays: group.Count()))
                    .OrderByDescending(row => row.Plays).ThenBy(row => PlayerName(catalog, row.Id)).ToArray();
                parts.Add(Ranking(game is null ? "Meeste overwinningen" : $"Meeste overwinningen bij {game.Name}", ranking,
                    row => $"{PlayerName(catalog, row.Id)} ({row.Plays} {(row.Plays == 1 ? "overwinning" : "overwinningen")})",
                    Limit(question, defaultLimit: 1)));
            }
            return string.Join(" ", parts);
        }

        var result = new List<string>();
        foreach (var action in actions.DistinctBy(action => action.Kind))
        {
            switch (action.Kind)
            {
                case "get_boardgames":
                    result.Add(game is not null ? $"{game.Name} staat in je collectie."
                        : catalog.Boardgames.Count == 0 ? "Je collectie is leeg."
                        : $"Bordspellen in je collectie: {string.Join(", ", catalog.Boardgames.Select(item => item.Name))}.");
                    break;
                case "get_players":
                    result.Add(catalog.Players.Count == 0 ? "Er zijn geen spelers geregistreerd."
                        : $"Spelers: {string.Join(", ", catalog.Players.Select(item => item.Name))}.");
                    break;
                case "get_expansions":
                    result.Add(catalog.Expansions.Count == 0 ? "Er zijn geen uitbreidingen geregistreerd."
                        : $"Uitbreidingen: {string.Join(", ", catalog.Expansions.Select(item => item.Name))}.");
                    break;
                case "get_sessions":
                    var sessions = await tools.CallAsync("get_game_sessions", game is null ? null : new { boardGameId = game.Id }, ct);
                    result.Add($"{(game is null ? "Er zijn" : $"Voor {game.Name} zijn")} {sessions.GetArrayLength()} potjes geregistreerd.");
                    break;
                case "get_player_statistics":
                case "get_leaderboard":
                    var players = await tools.CallAsync("get_player_statistics", game is null ? null : new { boardGameId = game.Id }, ct);
                    result.Add($"Spelerstatistieken: {string.Join(", ", players.EnumerateArray().Select(row => $"{row.GetProperty("playerName").GetString()} ({row.GetProperty("plays").GetInt32()} potjes, {row.GetProperty("wins").GetInt32()} overwinningen)"))}.");
                    break;
                case "get_boardgame_statistics":
                case "get_play_statistics":
                    var stats = await tools.CallAsync("get_boardgame_statistics", game is null ? null : new { boardGameId = game.Id }, ct);
                    result.Add($"Bordspelstatistieken: {string.Join(", ", stats.EnumerateArray().Select(row => $"{row.GetProperty("boardGameName").GetString()} ({row.GetProperty("plays").GetInt32()} potjes)"))}.");
                    break;
            }
        }
        return result.Count == 0 ? "Geen informatie gevonden." : string.Join(" ", result);
    }

    private static Item? FindGame(string question, Catalog catalog)
    {
        var direct = BoardgameMention.Find(catalog, question);
        if (direct.Game is not null) return direct.Game;
        if (direct.Ambiguous.Count > 0) return null;
        var words = Regex.Matches(question, @"[\p{L}\p{N}]+")
            .Select(match => match.Value).ToArray();
        var matches = new List<(Item Game, int Score, int Length)>();
        foreach (var game in catalog.Boardgames)
            for (var start = 0; start < words.Length; start++)
                for (var length = 1; length <= Math.Min(6, words.Length - start); length++)
                    if (GameTitleMatcher.SearchScore(string.Join(" ", words[start..(start + length)]), game.Name) is { } score)
                        matches.Add((game, score, length));
        var best = matches.OrderBy(match => match.Score).ThenByDescending(match => match.Length).FirstOrDefault();
        return best.Game is not null && matches.Count(match => match.Score == best.Score && match.Length == best.Length && match.Game.Id != best.Game.Id) == 0
            ? best.Game : null;
    }

    private static Item? FindPlayer(string question, Catalog catalog) => catalog.Players.FirstOrDefault(player =>
        Regex.IsMatch(question, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(player.Name)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase));

    private static int Limit(string question, int defaultLimit = 0)
    {
        var match = Regex.Match(question, @"\b(?:top|welke|de)\s+(?<count>\d+)\b", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["count"].Value, out var count) && count > 0 ? count : defaultLimit;
    }

    private static string Ranking(string title, (int Id, int Plays)[] ranking,
        Func<(int Id, int Plays), string> format, int limit)
    {
        if (ranking.Length == 0) return $"{title}: geen potjes gevonden.";
        var cutoff = limit <= 0 || limit >= ranking.Length ? 0 : ranking[limit - 1].Plays;
        var shown = cutoff == 0 ? ranking : ranking.Where(row => row.Plays >= cutoff).ToArray();
        return $"{title}: {string.Join(", ", shown.Select(format))}.";
    }

    private static string PlayerName(Catalog catalog, int id) => catalog.Players.FirstOrDefault(item => item.Id == id)?.Name ?? $"Speler {id}";
    private static string GameName(Catalog catalog, int id) => catalog.Boardgames.FirstOrDefault(item => item.Id == id)?.Name ?? $"Bordspel {id}";
    private sealed record Appearance(int PlayerId, bool IsWinner);
    private sealed record Session(int BoardGameId, Appearance[] Players);
}

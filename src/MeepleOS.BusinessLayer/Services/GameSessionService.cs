using System.Globalization;
using System.Text.Json;
using MeepleOS.BusinessLayer.Repositories;

namespace MeepleOS.BusinessLayer.Services;

public sealed record SessionPlayerInput(int PlayerId, int? Score = null, int? Position = null);

public sealed record SessionPlayerResult(int Id, int PlayerId, string PlayerName, int? Score,
    int? Position, bool IsWinner);

public sealed record GameSessionResult(int Id, int BoardGameId, string BoardGameName,
    DateOnly PlayedAt, string? Notes, List<SessionPlayerResult> Players);

public sealed class GameSessionService(IGameSessionRepository sessions)
{
    public async Task<GameSessionResult> RecordAsync(int boardGameId, DateOnly playedAt,
        IReadOnlyList<SessionPlayerInput> players, IReadOnlyCollection<int> winnerPlayerIds, string? notes = null)
    {
        await ValidateAsync(boardGameId, playedAt, players, winnerPlayerIds);
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        return await sessions.AddAsync(boardGameId, playedAt, notes, players, winnerPlayerIds.ToHashSet());
    }

    public async Task<GameSessionResult> UpdateAsync(int id, JsonElement changes)
    {
        if (id < 1)
            throw new ArgumentException("Geef een geldig potje-ID op.", nameof(id));
        var current = await sessions.GetByIdAsync(id)
            ?? throw new ArgumentException("Het potje bestaat niet.", nameof(id));

        if (changes.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Geef wijzigingen als object op.", nameof(changes));

        var boardGameId = current.BoardGameId;
        var playedAt = current.PlayedAt;
        var notes = current.Notes;
        var players = current.Players.Select(x => new SessionPlayerInput(x.PlayerId, x.Score, x.Position)).ToList();
        var winners = current.Players.Where(x => x.IsWinner).Select(x => x.PlayerId).ToList();
        var hasPlayers = changes.TryGetProperty("players", out _);
        var hasPlayerChanges = changes.TryGetProperty("playerChanges", out _);

        if (hasPlayers && hasPlayerChanges)
            throw new ArgumentException("Gebruik players of playerChanges, niet allebei.", nameof(changes));

        foreach (var property in changes.EnumerateObject())
        {
            switch (property.Name)
            {
                case "boardGameId":
                    boardGameId = ReadInt(property.Value, property.Name);
                    break;
                case "playedAt":
                    if (property.Value.ValueKind != JsonValueKind.String ||
                        !DateOnly.TryParseExact(property.Value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out playedAt))
                        throw new ArgumentException("Geef een speeldatum als YYYY-MM-DD op.", nameof(changes));
                    break;

                case "notes":
                    notes = property.Value.ValueKind == JsonValueKind.Null ? null
                        : property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()
                        : throw new ArgumentException("Opmerkingen moeten tekst of null zijn.", nameof(changes));
                    break;

                case "players":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                        throw new ArgumentException("Geef deelnemers als lijst op.", nameof(changes));
                    try
                    {
                        players = JsonSerializer.Deserialize<List<SessionPlayerInput>>(property.Value.GetRawText(),
                            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
                    }
                    catch (JsonException ex)
                    {
                        throw new ArgumentException("De deelnemerslijst is ongeldig.", nameof(changes), ex);
                    }
                    break;

                case "winnerPlayerIds":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                        throw new ArgumentException("Geef winnaars als lijst op.", nameof(changes));
                    winners = property.Value.EnumerateArray().Select(x => ReadInt(x, "winnerPlayerIds")).ToList();
                    break;

                case "playerChanges":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                        throw new ArgumentException("Geef spelerwijzigingen als lijst op.", nameof(changes));
                    var changedIds = new HashSet<int>();
                    foreach (var change in property.Value.EnumerateArray())
                    {
                        if (change.ValueKind != JsonValueKind.Object || !change.TryGetProperty("playerId", out var playerIdValue))
                            throw new ArgumentException("Elke spelerwijziging vereist een playerId.", nameof(changes));

                        var playerId = ReadInt(playerIdValue, "playerId");
                        if (!changedIds.Add(playerId))
                            throw new ArgumentException("Een deelnemer mag maar eenmaal worden gewijzigd.", nameof(changes));
                        var index = players.FindIndex(x => x.PlayerId == playerId);
                        if (index < 0)
                            throw new ArgumentException("De gewijzigde speler neemt niet deel aan dit potje.", nameof(changes));
                        var score = players[index].Score;
                        var position = players[index].Position;

                        foreach (var field in change.EnumerateObject())
                        {
                            if (field.Name == "score")
                                score = ReadNullableInt(field.Value, "score");
                            else if (field.Name == "position")
                                position = ReadNullableInt(field.Value, "position");
                            else if (field.Name != "playerId")
                                throw new ArgumentException($"Onbekend spelerveld: {field.Name}.", nameof(changes));
                        }

                        players[index] = new(playerId, score, position);
                    }
                    break;
                default:
                    throw new ArgumentException($"Onbekend wijzigingsveld: {property.Name}.", nameof(changes));
            }
        }

        await ValidateAsync(boardGameId, playedAt, players, winners);
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        return await sessions.UpdateAsync(id, boardGameId, playedAt, notes, players, winners.ToHashSet())
            ?? throw new ArgumentException("Het potje bestaat niet meer.", nameof(id));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (id < 1)
            throw new ArgumentException("Geef een geldig potje-ID op.", nameof(id));
        if (!await sessions.DeleteAsync(id))
            throw new ArgumentException("Het potje bestaat niet.", nameof(id));

        return true;
    }

    private static int ReadInt(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result : throw new ArgumentException($"{field} moet een geheel getal zijn.");

    private static int? ReadNullableInt(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Null ? null : ReadInt(value, field);

    private async Task ValidateAsync(int boardGameId, DateOnly playedAt,
        IReadOnlyList<SessionPlayerInput> players, IReadOnlyCollection<int> winnerPlayerIds)
    {
        if (boardGameId < 1)
            throw new ArgumentException("Selecteer een geldig bordspel.", nameof(boardGameId));

        if (playedAt == default)
            throw new ArgumentException("Geef een geldige speeldatum op.", nameof(playedAt));
        if (players is null || players.Count == 0)
            throw new ArgumentException("Een potje moet minstens één deelnemer hebben.", nameof(players));
        if (winnerPlayerIds is null || winnerPlayerIds.Count == 0)
            throw new ArgumentException("Selecteer minstens één winnaar.", nameof(winnerPlayerIds));

        var playerIds = new HashSet<int>();
        foreach (var player in players)
        {
            if (player is null || player.PlayerId < 1)
                throw new ArgumentException("Alle deelnemers moeten een geldig speler-ID hebben.", nameof(players));
            if (!playerIds.Add(player.PlayerId))
                throw new ArgumentException("Een speler mag maar één keer aan hetzelfde potje deelnemen.", nameof(players));
            if (player.Position is < 1)
                throw new ArgumentException("Een eindpositie moet minimaal 1 zijn.", nameof(players));
        }

        var winners = new HashSet<int>();
        foreach (var winnerId in winnerPlayerIds)
        {
            if (!winners.Add(winnerId))
                throw new ArgumentException("Een winnaar mag maar één keer opgegeven worden.", nameof(winnerPlayerIds));
            if (!playerIds.Contains(winnerId))
                throw new ArgumentException("Iedere winnaar moet aan het potje deelnemen.", nameof(winnerPlayerIds));
        }

        if (!await sessions.BoardgameExistsAsync(boardGameId))
            throw new ArgumentException("Het bordspel bestaat niet.", nameof(boardGameId));

        var existingPlayerIds = await sessions.GetExistingPlayerIdsAsync(playerIds);
        if (!playerIds.IsSubsetOf(existingPlayerIds))
            throw new ArgumentException("Een of meer deelnemers bestaan niet.", nameof(players));

    }

    public Task<List<GameSessionResult>> GetAllAsync(int? boardGameId = null, int? playerId = null,
        DateOnly? from = null, DateOnly? to = null)
    {
        if (boardGameId is < 1)
            throw new ArgumentException("Selecteer een geldig bordspel.", nameof(boardGameId));

        if (playerId is < 1)
            throw new ArgumentException("Selecteer een geldige speler.", nameof(playerId));

        if ((from.HasValue && from.Value == default) || (to.HasValue && to.Value == default)
            || (from.HasValue && to.HasValue && from > to))
            throw new ArgumentException("De datumperiode is ongeldig.");

        return sessions.GetAllAsync(boardGameId, playerId, from, to);
    }
}

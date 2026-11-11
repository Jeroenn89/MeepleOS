using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components.Pages;

public partial class Manage
{
    private List<BoardgameResult> games = [];
    private List<GameExpansionResult> expansions = [];
    private List<PlayerResult> players = [];
    private List<GameSessionResult> sessions = [];
    private List<ParticipantRow> rows = [];
    private HashSet<int> selectedExpansionIds = [];

    private int editGameId;
    private string gameName = "";
    private int minPlayers = 2;
    private int? maxPlayers = 4;
    private int duration = 45;

    private int editExpansionId;
    private int expansionGameId;
    private string expansionName = "";
    private string expansionDescription = "";

    private int editPlayerId;
    private string playerName = "";

    private int editSessionId;
    private int sessionGameId;
    private DateTime sessionDate = DateTime.Today;
    private string notes = "";
    private int addPlayerId;

    private string message = "";
    private string error = "";
    private string? deleteKind;
    private int deleteId;

    protected override async Task OnInitializedAsync() => await Reload();

    private async Task Reload()
    {
        games = await GameService.GetAllAsync();
        expansions = await ExpansionService.GetAllAsync();
        players = await PlayerService.GetAllAsync();
        sessions = await SessionService.GetAllAsync();
        foreach (var row in rows)
            row.Name = players.FirstOrDefault(player => player.Id == row.Id)?.Name ?? row.Name;

        rows = rows.Where(row => players.Any(player => player.Id == row.Id)).ToList();

        if (expansionGameId == 0 && games.Count > 0)
            expansionGameId = games[0].Id;
    }

    private void LoadGame()
    {
        var game = games.FirstOrDefault(game => game.Id == editGameId);
        if (game is null)
            return;

        gameName = game.Name;
        minPlayers = game.MinPlayers;
        maxPlayers = game.MaxPlayers;
        duration = game.EstimatedMinutes;
    }

    private void LoadExpansion()
    {
        var expansion = expansions.FirstOrDefault(expansion => expansion.Id == editExpansionId);
        if (expansion is null)
            return;

        expansionGameId = expansion.BoardGameId;
        expansionName = expansion.Name;
        expansionDescription = expansion.Description ?? "";
    }

    private void LoadPlayer()
    {
        playerName = players.FirstOrDefault(player => player.Id == editPlayerId)?.Name ?? "";
    }

    private void LoadSession()
    {
        var session = sessions.FirstOrDefault(session => session.Id == editSessionId);
        if (session is null)
        {
            rows = [];
            selectedExpansionIds = [];
            addPlayerId = 0;
            return;
        }

        sessionGameId = session.BoardGameId;
        sessionDate = session.PlayedAt.ToDateTime(TimeOnly.MinValue);
        notes = session.Notes ?? "";
        selectedExpansionIds = session.Expansions.Select(expansion => expansion.Id).ToHashSet();
        rows = session.Players.Select(player => new ParticipantRow(player.PlayerId, player.PlayerName)
        {
            Winner = player.IsWinner,
            Score = player.Score,
            Position = player.Position
        }).ToList();
        addPlayerId = 0;
    }

    private void AddParticipant()
    {
        var player = players.FirstOrDefault(player => player.Id == addPlayerId);
        if (player is null || rows.Any(row => row.Id == player.Id))
            return;

        rows.Add(new ParticipantRow(player.Id, player.Name));
        addPlayerId = 0;
    }

    private void RemoveParticipant(int id) => rows.RemoveAll(row => row.Id == id);

    private void ToggleExpansion(int id, bool selected)
    {
        if (selected)
            selectedExpansionIds.Add(id);
        else
            selectedExpansionIds.Remove(id);
    }

    private void ConfirmDeleteGame() => ConfirmDelete("game", editGameId);
    private void ConfirmDeleteExpansion() => ConfirmDelete("expansion", editExpansionId);
    private void ConfirmDeletePlayer() => ConfirmDelete("player", editPlayerId);
    private void ConfirmDeleteSession() => ConfirmDelete("session", editSessionId);

    private void ConfirmDelete(string kind, int id)
    {
        deleteKind = kind;
        deleteId = id;
        error = "";
    }
    private async Task Run(Func<Task> action)
    {
        error = "";
        message = "";
        try
        {
            await action();
            await Reload();
            message = "Opgeslagen.";
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
    }
    private Task SaveGame() => Run(async () =>
    {
        if (editGameId == 0)
        {
            await GameService.AddAsync(gameName, minPlayers, maxPlayers, duration);
        }
        else
        {
            var changes = JsonSerializer.SerializeToElement(new
            {
                name = gameName,
                minPlayers,
                maxPlayers,
                estimatedMinutes = duration
            });
            await GameService.UpdateAsync(editGameId, changes);
        }
    });

    private Task SaveExpansion() => Run(async () =>
    {
        if (editExpansionId == 0)
        {
            await ExpansionService.AddAsync(expansionGameId, expansionName, expansionDescription);
        }
        else
        {
            var changes = JsonSerializer.SerializeToElement(new
            {
                name = expansionName,
                description = expansionDescription
            });
            await ExpansionService.UpdateAsync(editExpansionId, changes);
        }
    });

    private Task SavePlayer() => Run(async () =>
    {
        if (editPlayerId == 0)
            await PlayerService.AddAsync(playerName);
        else
            await PlayerService.UpdateAsync(editPlayerId, playerName);
    });

    private Task SaveSession() => Run(async () =>
    {
        var participants = rows.Select(row => new SessionPlayerInput(row.Id, row.Score, row.Position)).ToList();
        var winnerIds = rows.Where(row => row.Winner).Select(row => row.Id).ToList();
        var expansionIds = selectedExpansionIds.ToList();
        var playedAt = DateOnly.FromDateTime(sessionDate);

        if (editSessionId == 0)
        {
            await SessionService.RecordAsync(sessionGameId, playedAt, participants, winnerIds, notes, expansionIds);
        }
        else
        {
            var changes = JsonSerializer.SerializeToElement(new
            {
                boardGameId = sessionGameId,
                playedAt = playedAt.ToString("yyyy-MM-dd"),
                players = participants,
                winnerPlayerIds = winnerIds,
                notes,
                expansionIds
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await SessionService.UpdateAsync(editSessionId, changes);
        }
    });

    private async Task Delete()
    {
        var kind = deleteKind;
        deleteKind = null;
        await Run(async () =>
        {
            switch (kind)
            {
                case "game":
                    await GameService.DeleteAsync(deleteId);
                    editGameId = 0;
                    break;
                case "expansion":
                    await ExpansionService.DeleteAsync(deleteId);
                    editExpansionId = 0;
                    break;
                case "player":
                    await PlayerService.DeleteAsync(deleteId);
                    editPlayerId = 0;
                    break;
                case "session":
                    await SessionService.DeleteAsync(deleteId);
                    editSessionId = 0;
                    break;
            }
        });
    }

    private sealed class ParticipantRow(int id, string name)
    {
        public int Id { get; } = id;
        public string Name { get; set; } = name;
        public bool Winner { get; set; }
        public int? Score { get; set; }
        public int? Position { get; set; }
    }
}

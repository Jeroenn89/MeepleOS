using System.Text.Json;
using MeepleOS.BusinessLayer.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

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
    private string editGameSearch = "";
    private string gameName = "";
    private int minPlayers = 2;
    private int? maxPlayers = 4;
    private int duration = 45;

    private int editExpansionId;
    private int expansionGameId;
    private string expansionGameSearch = "";
    private string expansionName = "";
    private string expansionDescription = "";

    private int editPlayerId;
    private string playerName = "";

    private int editSessionId;
    private int sessionGameId;
    private string sessionGameSearch = "";
    private DateTime sessionDate = DateTime.Today;
    private string notes = "";
    private int addPlayerId;

    private string message = "";
    private string error = "";
    private string? deleteKind;
    private int deleteId;
    private ElementReference cancelDeleteButton;
    private bool focusDeleteDialog;
    private int openSection = -1;

    private string DeleteKindLabel => deleteKind switch
    {
        "game" => "Bordspel",
        "expansion" => "Uitbreiding",
        "player" => "Speler",
        "session" => "Potje",
        _ => "Item"
    };

    private string DeleteItemName => deleteKind switch
    {
        "game" => games.FirstOrDefault(game => game.Id == deleteId)?.Name ?? $"bordspel #{deleteId}",
        "expansion" => expansions.FirstOrDefault(expansion => expansion.Id == deleteId)?.Name ?? $"uitbreiding #{deleteId}",
        "player" => players.FirstOrDefault(player => player.Id == deleteId)?.Name ?? $"speler #{deleteId}",
        "session" => sessions.FirstOrDefault(session => session.Id == deleteId) is { } session
            ? $"{session.BoardGameName} van {session.PlayedAt:dd-MM-yyyy}"
            : $"potje #{deleteId}",
        _ => $"item #{deleteId}"
    };

    private void OpenSection(int section) => openSection = openSection == section ? -1 : section;

    private IEnumerable<BoardgameResult> MatchingGames(string searchTerm) =>
        games.Where(game => game.Name.Contains(searchTerm.Trim(), StringComparison.OrdinalIgnoreCase)).Take(8);

    private void SearchEditGame(ChangeEventArgs eventArgs)
    {
        editGameSearch = eventArgs.Value?.ToString() ?? "";
        if (editGameId == 0)
            return;

        editGameId = 0;
        LoadGame();
    }

    private void ChooseEditGame(int id)
    {
        var game = games.First(game => game.Id == id);
        editGameId = id;
        editGameSearch = game.Name;
        LoadGame();
    }

    private void StartNewGame()
    {
        editGameSearch = "";
        editGameId = 0;
        LoadGame();
    }

    private void SearchExpansionGame(ChangeEventArgs eventArgs)
    {
        expansionGameSearch = eventArgs.Value?.ToString() ?? "";
        expansionGameId = 0;
    }

    private void ChooseExpansionGame(int id)
    {
        expansionGameId = id;
        expansionGameSearch = games.First(game => game.Id == id).Name;
    }

    private void SearchSessionGame(ChangeEventArgs eventArgs)
    {
        sessionGameSearch = eventArgs.Value?.ToString() ?? "";
        if (sessionGameId != 0)
            selectedExpansionIds.Clear();
        sessionGameId = 0;
    }

    private void ChooseSessionGame(int id)
    {
        sessionGameId = id;
        sessionGameSearch = games.First(game => game.Id == id).Name;
    }

    protected override async Task OnInitializedAsync() => await Reload();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!focusDeleteDialog)
            return;

        focusDeleteDialog = false;
        await cancelDeleteButton.FocusAsync();
    }

    private async Task Reload()
    {
        games = await GameService.GetAllAsync();
        expansions = await ExpansionService.GetAllAsync();
        players = await PlayerService.GetAllAsync();
        sessions = await SessionService.GetAllAsync();
        foreach (var row in rows)
            row.Name = players.FirstOrDefault(player => player.Id == row.Id)?.Name ?? row.Name;

        rows = rows.Where(row => players.Any(player => player.Id == row.Id)).ToList();

    }

    private void LoadGame()
    {
        var game = games.FirstOrDefault(game => game.Id == editGameId);
        if (game is null)
        {
            gameName = "";
            minPlayers = 2;
            maxPlayers = 4;
            duration = 45;
            return;
        }

        gameName = game.Name;
        minPlayers = game.MinPlayers;
        maxPlayers = game.MaxPlayers;
        duration = game.EstimatedMinutes;
    }

    private void LoadExpansion()
    {
        var expansion = expansions.FirstOrDefault(expansion => expansion.Id == editExpansionId);
        if (expansion is null)
        {
            expansionGameId = 0;
            expansionGameSearch = "";
            expansionName = "";
            expansionDescription = "";
            return;
        }

        expansionGameId = expansion.BoardGameId;
        expansionGameSearch = games.FirstOrDefault(game => game.Id == expansionGameId)?.Name ?? "";
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
            sessionGameId = 0;
            sessionGameSearch = "";
            sessionDate = DateTime.Today;
            notes = "";
            rows = [];
            selectedExpansionIds = [];
            addPlayerId = 0;
            return;
        }

        sessionGameId = session.BoardGameId;
        sessionGameSearch = games.FirstOrDefault(game => game.Id == sessionGameId)?.Name ?? "";
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
        focusDeleteDialog = true;
        error = "";
    }

    private void CancelDelete() => deleteKind = null;

    private void HandleDeleteDialogKeyDown(KeyboardEventArgs eventArgs)
    {
        if (eventArgs.Key == "Escape")
            CancelDelete();
    }

    private async Task Run(Func<Task> action, string successMessage = "Opgeslagen.")
    {
        error = "";
        message = "";
        try
        {
            await action();
            await Reload();
            message = successMessage;
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
                    StartNewGame();
                    break;
                case "expansion":
                    await ExpansionService.DeleteAsync(deleteId);
                    editExpansionId = 0;
                    LoadExpansion();
                    break;
                case "player":
                    await PlayerService.DeleteAsync(deleteId);
                    editPlayerId = 0;
                    LoadPlayer();
                    break;
                case "session":
                    await SessionService.DeleteAsync(deleteId);
                    editSessionId = 0;
                    LoadSession();
                    break;
            }
        }, "Verwijderd.");
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

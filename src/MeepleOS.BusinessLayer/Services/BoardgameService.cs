using MeepleOS.BusinessLayer.Repositories;

namespace MeepleOS.BusinessLayer.Services;

public sealed record BoardgameResult(int Id, string Name, int MinPlayers, int MaxPlayers, int EstimatedMinutes);

public sealed class BoardgameService(IBoardgameRepository boardgames)
{
    public async Task<BoardgameResult> AddAsync(string name, int minPlayers, int maxPlayers, int estimatedMinutes)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 200)
            throw new ArgumentException("De naam moet 1 tot 200 tekens bevatten.", nameof(name));
        if (minPlayers < 1 || maxPlayers < minPlayers)
            throw new ArgumentException("Het spelersaantal moet minimaal 1 zijn en het maximum mag niet onder het minimum liggen.");
        if (estimatedMinutes < 1)
            throw new ArgumentException("De geschatte speelduur moet minimaal 1 minuut zijn.", nameof(estimatedMinutes));

        return await boardgames.AddAsync(name, minPlayers, maxPlayers, estimatedMinutes);
    }

    public Task<List<BoardgameResult>> GetAllAsync() => boardgames.GetAllAsync();
}

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

    public async Task<BoardgameResult> UpdateAsync(int id, string? name = null, int? minPlayers = null,
        int? maxPlayers = null, int? estimatedMinutes = null)
    {
        if (id < 1)
            throw new ArgumentException("Geef een geldig bordspel-ID op.", nameof(id));

        var current = await boardgames.GetByIdAsync(id)
            ?? throw new ArgumentException("Het bordspel bestaat niet.", nameof(id));

        var newName = name is null ? current.Name : name.Trim();
        var newMin = minPlayers ?? current.MinPlayers;
        var newMax = maxPlayers ?? current.MaxPlayers;
        var newMinutes = estimatedMinutes ?? current.EstimatedMinutes;

        if (newName.Length is < 1 or > 200)
            throw new ArgumentException("De naam moet 1 tot 200 tekens bevatten.", nameof(name));

        if (newMin < 1 || newMax < newMin)
            throw new ArgumentException("Het spelersaantal moet minimaal 1 zijn en het maximum mag niet onder het minimum liggen.");

        if (newMinutes < 1)
            throw new ArgumentException("De geschatte speelduur moet minimaal 1 minuut zijn.", nameof(estimatedMinutes));

        return await boardgames.UpdateAsync(id, newName, newMin, newMax, newMinutes)
            ?? throw new ArgumentException("Het bordspel bestaat niet meer.", nameof(id));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (id < 1)
            throw new ArgumentException("Geef een geldig bordspel-ID op.", nameof(id));

        if (await boardgames.GetByIdAsync(id) is null)
            throw new ArgumentException("Het bordspel bestaat niet.", nameof(id));

        if (await boardgames.HasSessionsAsync(id))
            throw new InvalidOperationException("Dit bordspel kan niet worden verwijderd: er zijn gespeelde potjes aan gekoppeld.");

        if (await boardgames.HasExpansionsAsync(id))
            throw new InvalidOperationException("Dit bordspel kan niet worden verwijderd: er zijn uitbreidingen aan gekoppeld.");

        return await boardgames.DeleteAsync(id);
    }
}

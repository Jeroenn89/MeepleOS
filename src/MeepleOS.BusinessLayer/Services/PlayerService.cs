using MeepleOS.BusinessLayer.Repositories;

namespace MeepleOS.BusinessLayer.Services;

public sealed record PlayerResult(int Id, string Name);

public sealed class PlayerService(IPlayerRepository players)
{
    public async Task<PlayerResult> AddAsync(string name)
    {
        name = name?.Trim() ?? "";

        if (name.Length is < 1 or > 200)
            throw new ArgumentException("De naam moet 1 tot 200 tekens bevatten.", nameof(name));

        return await players.AddOrGetAsync(name, name.ToUpperInvariant());
    }

    public Task<List<PlayerResult>> GetAllAsync() => players.GetAllAsync();

    public async Task<PlayerResult> UpdateAsync(int id, string name)
    {
        if (id < 1)
            throw new ArgumentException("Geef een geldig speler-ID op.", nameof(id));

        if (await players.GetByIdAsync(id) is null)
            throw new ArgumentException("De speler bestaat niet.", nameof(id));

        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 200)
            throw new ArgumentException("De naam moet 1 tot 200 tekens bevatten.", nameof(name));

        var normalizedName = name.ToUpperInvariant();
        if (await players.NameExistsAsync(normalizedName, id))
            throw new ArgumentException("Er bestaat al een speler met deze naam.", nameof(name));

        return await players.UpdateAsync(id, name, normalizedName)
            ?? throw new ArgumentException("De speler bestaat niet meer.", nameof(id));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (id < 1)
            throw new ArgumentException("Geef een geldig speler-ID op.", nameof(id));

        if (await players.GetByIdAsync(id) is null)
            throw new ArgumentException("De speler bestaat niet.", nameof(id));

        if (await players.HasSessionsAsync(id))
            throw new InvalidOperationException("Deze speler kan niet worden verwijderd: de speler komt voor in gespeelde potjes.");

        return await players.DeleteAsync(id);
    }
}

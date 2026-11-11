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
}

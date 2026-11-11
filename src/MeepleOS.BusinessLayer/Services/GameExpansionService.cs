using System.Text.Json;
using MeepleOS.BusinessLayer.Repositories;

namespace MeepleOS.BusinessLayer.Services;

public sealed record GameExpansionResult(int Id, int BoardGameId, string BoardGameName,
    string Name, string? Description);

public sealed class GameExpansionService(IGameExpansionRepository expansions, IBoardgameRepository boardgames)
{
    public async Task<GameExpansionResult> AddAsync(int boardGameId, string name, string? description = null)
    {
        if (boardGameId < 1 || await boardgames.GetByIdAsync(boardGameId) is null)
            throw new ArgumentException("Het bordspel bestaat niet.", nameof(boardGameId));
        name = ValidateName(name);
        var normalizedName = Normalize(name);
        if (await expansions.NameExistsAsync(boardGameId, normalizedName))
            throw new ArgumentException("Deze uitbreiding bestaat al bij dit bordspel.", nameof(name));
        return await expansions.AddAsync(boardGameId, name, normalizedName, Clean(description));
    }

    public Task<List<GameExpansionResult>> GetAllAsync(int? boardGameId = null)
    {
        if (boardGameId is < 1)
            throw new ArgumentException("Selecteer een geldig bordspel.", nameof(boardGameId));
        return expansions.GetAllAsync(boardGameId);
    }

    public async Task<GameExpansionResult> UpdateAsync(int id, JsonElement changes)
    {
        if (id < 1)
            throw new ArgumentException("Geef een geldig uitbreiding-ID op.", nameof(id));
        var current = await expansions.GetByIdAsync(id)
            ?? throw new ArgumentException("De uitbreiding bestaat niet.", nameof(id));
        if (changes.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Geef wijzigingen als object op.", nameof(changes));
        var name = current.Name;
        var description = current.Description;
        foreach (var property in changes.EnumerateObject())
        {
            switch (property.Name)
            {
                case "name":
                    if (property.Value.ValueKind != JsonValueKind.String)
                        throw new ArgumentException("De naam moet tekst zijn.", nameof(changes));
                    name = ValidateName(property.Value.GetString());
                    break;
                case "description":
                    description = property.Value.ValueKind == JsonValueKind.Null ? null
                        : property.Value.ValueKind == JsonValueKind.String ? Clean(property.Value.GetString())
                        : throw new ArgumentException("De beschrijving moet tekst of null zijn.", nameof(changes));
                    break;
                default:
                    throw new ArgumentException($"Onbekend wijzigingsveld: {property.Name}.", nameof(changes));
            }
        }
        var normalizedName = Normalize(name);
        if (await expansions.NameExistsAsync(current.BoardGameId, normalizedName, id))
            throw new ArgumentException("Deze uitbreiding bestaat al bij dit bordspel.", nameof(changes));
        return await expansions.UpdateAsync(id, name, normalizedName, description)
            ?? throw new ArgumentException("De uitbreiding bestaat niet meer.", nameof(id));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (id < 1 || await expansions.GetByIdAsync(id) is null)
            throw new ArgumentException("De uitbreiding bestaat niet.", nameof(id));
        if (await expansions.HasSessionsAsync(id))
            throw new InvalidOperationException("Deze uitbreiding kan niet worden verwijderd: ze is gebruikt in gespeelde potjes.");
        return await expansions.DeleteAsync(id);
    }

    private static string ValidateName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
            throw new ArgumentException("De naam moet 1 tot 200 tekens bevatten.", nameof(name));
        return name;
    }

    private static string Normalize(string name) => name.ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

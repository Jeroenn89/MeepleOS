using System.Text.Json;
using MeepleOS.BusinessLayer.Repositories;

namespace MeepleOS.BusinessLayer.Services;

public sealed record BoardgameResult(int Id, string Name, int MinPlayers, int? MaxPlayers, int EstimatedMinutes);

public sealed class BoardgameService(IBoardgameRepository boardgames)
{
    public async Task<BoardgameResult> AddAsync(string name, int minPlayers, int? maxPlayers, int estimatedMinutes)
    {
        name = name?.Trim() ?? "";

        if (name.Length is < 1 or > 200)
            throw new ArgumentException("De naam moet 1 tot 200 tekens bevatten.", nameof(name));

        if (minPlayers < 1 || (maxPlayers.HasValue && maxPlayers.Value < minPlayers))
            throw new ArgumentException("Het spelersaantal moet minimaal 1 zijn en het maximum mag niet onder het minimum liggen.");

        if (estimatedMinutes < 1)
            throw new ArgumentException("De geschatte speelduur moet minimaal 1 minuut zijn.", nameof(estimatedMinutes));

        return await boardgames.AddAsync(name, minPlayers, maxPlayers, estimatedMinutes);
    }

    public Task<List<BoardgameResult>> GetAllAsync() => boardgames.GetAllAsync();

    public async Task<BoardgameResult> UpdateAsync(int id, JsonElement changes)
    {
        if (id < 1)
            throw new ArgumentException("Geef een geldig bordspel-ID op.", nameof(id));

        var current = await boardgames.GetByIdAsync(id)
            ?? throw new ArgumentException("Het bordspel bestaat niet.", nameof(id));

        if (changes.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Geef wijzigingen als object op.", nameof(changes));

        var newName = current.Name;
        var newMin = current.MinPlayers;
        var newMax = current.MaxPlayers;
        var newMinutes = current.EstimatedMinutes;
        foreach (var property in changes.EnumerateObject())
        {
            switch (property.Name)
            {
                case "name":
                    if (property.Value.ValueKind != JsonValueKind.String)
                        throw new ArgumentException("De naam moet tekst zijn.", nameof(changes));
                    newName = property.Value.GetString()!.Trim();
                    break;
                case "minPlayers":
                    newMin = ReadInt(property.Value, "Het minimum", changes);
                    break;
                case "maxPlayers":
                    newMax = property.Value.ValueKind == JsonValueKind.Null ? null
                        : ReadInt(property.Value, "Het maximum", changes);
                    break;
                case "estimatedMinutes":
                    newMinutes = ReadInt(property.Value, "De geschatte speelduur", changes);
                    break;
                default:
                    throw new ArgumentException($"Onbekend wijzigingsveld: {property.Name}.", nameof(changes));
            }
        }

        if (newName.Length is < 1 or > 200)
            throw new ArgumentException("De naam moet 1 tot 200 tekens bevatten.", nameof(changes));

        if (newMin < 1 || (newMax.HasValue && newMax.Value < newMin))
            throw new ArgumentException("Het spelersaantal moet minimaal 1 zijn en het maximum mag niet onder het minimum liggen.");

        if (newMinutes < 1)
            throw new ArgumentException("De geschatte speelduur moet minimaal 1 minuut zijn.", nameof(changes));

        return await boardgames.UpdateAsync(id, newName, newMin, newMax, newMinutes)
            ?? throw new ArgumentException("Het bordspel bestaat niet meer.", nameof(id));
    }

    private static int ReadInt(JsonElement value, string label, JsonElement changes)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new ArgumentException($"{label} moet een geheel getal zijn.", nameof(changes));
        return result;
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

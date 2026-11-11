namespace MeepleOS.DataLayer.Data.Models;

public sealed class GameExpansion
{
    public int Id { get; set; }
    public int BoardGameId { get; set; }
    public Boardgame BoardGame { get; set; } = null!;
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public string? Description { get; set; }
}

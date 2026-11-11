namespace MeepleOS.DataLayer.Data.Models;

public sealed class Player
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
}

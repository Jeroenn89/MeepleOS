namespace MeepleOS.DataLayer.Data.Models;

public sealed class Player
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public string? AccountId { get; set; }
    public MeepleUser? Account { get; set; }
}

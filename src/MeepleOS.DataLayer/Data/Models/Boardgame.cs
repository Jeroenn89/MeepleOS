namespace MeepleOS.DataLayer.Data.Models;

public sealed class Boardgame
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int EstimatedMinutes { get; set; }
}

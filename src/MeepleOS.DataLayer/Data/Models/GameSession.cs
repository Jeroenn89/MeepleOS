namespace MeepleOS.DataLayer.Data.Models;

public sealed class GameSession
{
    public int Id { get; set; }
    public int BoardGameId { get; set; }
    public Boardgame BoardGame { get; set; } = null!;
    public DateOnly PlayedAt { get; set; }
    public string? Notes { get; set; }
    public List<SessionPlayer> Players { get; set; } = [];
    public List<GameSessionExpansion> Expansions { get; set; } = [];
}

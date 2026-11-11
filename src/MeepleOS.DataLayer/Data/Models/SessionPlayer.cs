namespace MeepleOS.DataLayer.Data.Models;

public sealed class SessionPlayer
{
    public int Id { get; set; }
    public int GameSessionId { get; set; }
    public GameSession GameSession { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public int? Score { get; set; }
    public int? Position { get; set; }
    public bool IsWinner { get; set; }
}

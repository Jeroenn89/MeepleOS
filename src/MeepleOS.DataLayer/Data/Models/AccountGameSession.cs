namespace MeepleOS.DataLayer.Data.Models;

public sealed class AccountGameSession
{
    public required string AccountId { get; set; }
    public MeepleUser Account { get; set; } = null!;
    public int GameSessionId { get; set; }
    public GameSession GameSession { get; set; } = null!;
}

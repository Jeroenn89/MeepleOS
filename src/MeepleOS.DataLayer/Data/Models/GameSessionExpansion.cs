namespace MeepleOS.DataLayer.Data.Models;

public sealed class GameSessionExpansion
{
    public int GameSessionId { get; set; }
    public GameSession GameSession { get; set; } = null!;
    public int GameExpansionId { get; set; }
    public GameExpansion GameExpansion { get; set; } = null!;
}

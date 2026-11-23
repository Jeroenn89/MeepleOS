namespace MeepleOS.DataLayer.Data.Models;

public sealed class AccountBoardgame
{
    public required string AccountId { get; set; }
    public MeepleUser Account { get; set; } = null!;
    public int BoardGameId { get; set; }
    public Boardgame BoardGame { get; set; } = null!;
}

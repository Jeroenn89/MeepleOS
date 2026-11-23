namespace MeepleOS.DataLayer.Data.Models;

public sealed class AccountPlayer
{
    public required string AccountId { get; set; }
    public MeepleUser Account { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
}

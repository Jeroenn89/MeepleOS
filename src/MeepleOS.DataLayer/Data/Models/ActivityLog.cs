namespace MeepleOS.DataLayer.Data.Models;

public sealed class ActivityLog
{
    public long Id { get; set; }
    public Guid? RequestId { get; set; }
    public required string AccountId { get; set; }
    public string? AccountReferenceId { get; set; }
    public MeepleUser? Account { get; set; }
    public required string AccountName { get; set; }
    public required string ToolName { get; set; }
    public required string Parameters { get; set; }
    public required string Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

using System.Text.Json.Serialization;

namespace MeepleOS.AiClient;

public sealed record Item(int Id, string Name, int? BoardGameId = null);
public sealed record Catalog(List<Item> Boardgames, List<Item> Players, List<Item> Expansions);

public sealed class Plan
{
    public string? Question { get; set; }
    public List<ActionPlan> Actions { get; set; } = [];
}

public sealed class ActionPlan
{
    public string Kind { get; set; } = "";
    public string? Name { get; set; }
    public int? Id { get; set; }
    public string? Boardgame { get; set; }
    public int? MinPlayers { get; set; }
    public int? MaxPlayers { get; set; }
    public bool NoMaximum { get; set; }
    public int? EstimatedMinutes { get; set; }
    public string? Description { get; set; }
    public string? PlayedAt { get; set; }
    public List<ParticipantPlan> Players { get; set; } = [];
    public List<string> Winners { get; set; } = [];
    public List<string> Expansions { get; set; } = [];
    public string? Notes { get; set; }
    public string? ChangesJson { get; set; }
}

public sealed class ParticipantPlan
{
    public string Name { get; set; } = "";
    public int? Score { get; set; }
    public int? Position { get; set; }
}

public sealed record CommandResult(string Message, int GeminiRequests);

public interface IMeepleTools
{
    Task<System.Text.Json.JsonElement> CallAsync(string tool, object? arguments = null, CancellationToken ct = default);
}

public interface IPlanner
{
    Task<Plan> PlanAsync(string command, Catalog catalog, DateOnly today, CancellationToken ct = default);
}

public interface IRequestCounter { int RequestCount { get; } }

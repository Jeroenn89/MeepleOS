namespace MeepleOS.AiClient;

public sealed record GameSearchHit(string Title, string Url, string Content);
public sealed class GameSearchUnavailableException(string message) : Exception(message);

public interface IGameSearch
{
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, CancellationToken ct = default);
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts, CancellationToken ct = default)
        => SearchAsync(game, ct);
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, IReadOnlyCollection<string> missingFacts,
        int? preferredBggId, IReadOnlyCollection<int> excludedBggIds, CancellationToken ct = default)
        => SearchAsync(game, missingFacts, ct);
    Task<IReadOnlyList<GameSearchHit>> SearchAsync(string game, string? alternativeGameName,
        IReadOnlyCollection<string> missingFacts, int? preferredBggId,
        IReadOnlyCollection<int> excludedBggIds, CancellationToken ct = default)
        => SearchAsync(game, missingFacts, preferredBggId, excludedBggIds, ct);
}

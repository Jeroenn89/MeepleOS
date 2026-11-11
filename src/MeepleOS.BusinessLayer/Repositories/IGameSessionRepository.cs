using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.BusinessLayer.Repositories;

public interface IGameSessionRepository
{
    Task<bool> BoardgameExistsAsync(int boardGameId);
    Task<HashSet<int>> GetExistingPlayerIdsAsync(IReadOnlyCollection<int> playerIds);
    Task<HashSet<int>> GetExistingExpansionIdsAsync(int boardGameId, IReadOnlyCollection<int> expansionIds);
    Task<GameSessionResult> AddAsync(int boardGameId, DateOnly playedAt, string? notes,
        IReadOnlyList<SessionPlayerInput> players, IReadOnlySet<int> winnerPlayerIds,
        IReadOnlyCollection<int> expansionIds);
    Task<List<GameSessionResult>> GetAllAsync(int? boardGameId, int? playerId, DateOnly? from, DateOnly? to);
    Task<GameSessionResult?> GetByIdAsync(int id);
    Task<GameSessionResult?> UpdateAsync(int id, int boardGameId, DateOnly playedAt, string? notes,
        IReadOnlyList<SessionPlayerInput> players, IReadOnlySet<int> winnerPlayerIds,
        IReadOnlyCollection<int> expansionIds);
    Task<bool> DeleteAsync(int id);
}

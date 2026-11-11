using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.BusinessLayer.Repositories;

public interface IGameExpansionRepository
{
    Task<GameExpansionResult> AddAsync(int boardGameId, string name, string normalizedName, string? description);
    Task<List<GameExpansionResult>> GetAllAsync(int? boardGameId);
    Task<GameExpansionResult?> GetByIdAsync(int id);
    Task<bool> NameExistsAsync(int boardGameId, string normalizedName, int exceptId = 0);
    Task<GameExpansionResult?> UpdateAsync(int id, string name, string normalizedName, string? description);
    Task<bool> HasSessionsAsync(int id);
    Task<bool> DeleteAsync(int id);
}

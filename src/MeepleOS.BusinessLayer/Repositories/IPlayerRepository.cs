using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.BusinessLayer.Repositories;

public interface IPlayerRepository
{
    Task<PlayerResult> AddOrGetAsync(string name, string normalizedName);
    Task<List<PlayerResult>> GetAllAsync();
    Task<PlayerResult?> GetByIdAsync(int id);
    Task<bool> NameExistsAsync(string normalizedName, int exceptId);
    Task<PlayerResult?> UpdateAsync(int id, string name, string normalizedName);
    Task<bool> DeleteAsync(int id);
    Task<bool> HasSessionsAsync(int id);
}

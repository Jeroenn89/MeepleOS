using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.BusinessLayer.Repositories;

public interface IPlayerRepository
{
    Task<PlayerResult> AddOrGetAsync(string name, string normalizedName);
    Task<List<PlayerResult>> GetAllAsync();
}

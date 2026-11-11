using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.BusinessLayer.Repositories;

public interface IBoardgameRepository
{
    Task<BoardgameResult> AddAsync(string name, int minPlayers, int maxPlayers, int estimatedMinutes);
    Task<List<BoardgameResult>> GetAllAsync();
    Task<BoardgameResult?> GetByIdAsync(int id);
    Task<BoardgameResult?> UpdateAsync(int id, string name, int minPlayers, int maxPlayers, int estimatedMinutes);
    Task<bool> DeleteAsync(int id);
    Task<bool> HasSessionsAsync(int id);
}

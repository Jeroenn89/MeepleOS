using MeepleOS.BusinessLayer.Services;

namespace MeepleOS.BusinessLayer.Repositories;

public interface IBoardgameRepository
{
    Task<BoardgameResult> AddAsync(string name, int minPlayers, int maxPlayers, int estimatedMinutes);
    Task<List<BoardgameResult>> GetAllAsync();
}

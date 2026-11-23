namespace MeepleOS.BusinessLayer.Services;

public interface IAccountContext
{
    Task<string> GetAccountIdAsync();
}

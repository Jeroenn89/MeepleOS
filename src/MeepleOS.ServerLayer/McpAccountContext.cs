using MeepleOS.BusinessLayer.Services;
using Microsoft.AspNetCore.DataProtection;

namespace MeepleOS.ServerLayer;

public sealed class McpAccountContext(string token, ITimeLimitedDataProtector protector) : IAccountContext
{
    public Task<string> GetAccountIdAsync() => Task.FromResult(protector.Unprotect(token));
}

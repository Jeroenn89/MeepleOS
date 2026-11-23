using MeepleOS.BusinessLayer.Services;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace MeepleOS.Web.Services;

public sealed class BlazorAccountContext(AuthenticationStateProvider authentication) : IAccountContext
{
    public async Task<string> GetAccountIdAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Log eerst in.");
    }
}

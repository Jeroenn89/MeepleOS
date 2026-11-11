using MeepleOS.BusinessLayer.Services;
using MeepleOS.Web.Services;
using Microsoft.AspNetCore.Components;

namespace MeepleOS.Web.Components.Pages;

public partial class SessionDetail
{
    [Parameter]
    public int Id
    {
        get;
        set;
    }
    GameSessionResult? session;
    protected override async Task OnParametersSetAsync()
    {
        session = (await Sessions.GetAllAsync()).FirstOrDefault(x => x.Id == Id);
    }
}

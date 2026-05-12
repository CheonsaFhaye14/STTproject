using Microsoft.AspNetCore.Components;
using STTproject.Services;

namespace STTproject.Features.Admin.Dashboard.Components.Pages;

public partial class Admin
{
    [Inject] private IUserContextService UserContext { get; set; } = default!;
    [Inject] private IHomeService HomeService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        if (!UserContext.UserId.HasValue)
        {
            Navigation.NavigateTo("/", forceLoad: true);
            return;
        }

        var user = await HomeService.GetUserAsync(UserContext.UserId.Value);
        if (user == null || !string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            Navigation.NavigateTo("/home", forceLoad: true);
        }
    }
}
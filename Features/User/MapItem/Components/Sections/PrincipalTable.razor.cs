using Microsoft.AspNetCore.Components;

namespace STTproject.Features.User.MapItem.Components.Sections;

public partial class PrincipalTable
{
    [Parameter]
    public IReadOnlyList<string> Principals { get; set; } = Array.Empty<string>();

    [Parameter]
    public string? SelectedPrincipal { get; set; }

    [Parameter]
    public EventCallback<string?> SelectedPrincipalChanged { get; set; }

    [Parameter]
    public EventCallback<string?> OnPrincipalSelected { get; set; }

    private async Task SelectPrincipalAsync(string? principal)
    {
        SelectedPrincipal = principal;

        if (SelectedPrincipalChanged.HasDelegate)
        {
            await SelectedPrincipalChanged.InvokeAsync(principal);
        }

        if (OnPrincipalSelected.HasDelegate)
        {
            await OnPrincipalSelected.InvokeAsync(principal);
        }
    }
}

using Microsoft.AspNetCore.Components;
using STTproject.Data;
using STTproject.Models;

namespace STTproject.Features.User.MapItem.Components.Modals;

public partial class DownloadTemplate
{
    [Parameter]
    public bool ShowModal { get; set; }

    [Parameter]
    public List<SubDistributor> SubdList { get; set; } = new();

    [Parameter]
    public List<string> PrincipalList { get; set; } = new();

    [Parameter]
    public EventCallback<(int SubdistributorId, string? Principal)> OnDownload { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private int selectedSubdistributorId = 0;
    private string? selectedPrincipal = string.Empty;

    private async Task DownloadClicked()
    {
        await OnDownload.InvokeAsync((selectedSubdistributorId, selectedPrincipal));
    }

    private async Task CancelClicked()
    {
        selectedSubdistributorId = 0;
        selectedPrincipal = string.Empty;
        await OnCancel.InvokeAsync();
    }
}
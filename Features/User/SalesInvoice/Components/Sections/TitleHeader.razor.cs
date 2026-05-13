using Microsoft.AspNetCore.Components;
using STTproject.Data;
using System.Linq;

namespace STTproject.Features.User.SalesInvoice.Components.Sections;

public partial class TitleHeader : ComponentBase
{
    [Parameter] public List<SubDistributor> Subdistributors { get; set; } = new();

    [Parameter] public int SelectedSubdId { get; set; }
    [Parameter] public EventCallback<int> SelectedSubdIdChanged { get; set; }

    [Parameter] public EventCallback<SubDistributor> OnSubdChanged { get; set; }
    [Parameter] public bool IsSaved { get; set; } = false;

    string SelectedSubdName = "";

    protected override void OnParametersSet()
    {
        UpdateDisplay();
    }

    async Task HandleChange()
    {
        await SelectedSubdIdChanged.InvokeAsync(SelectedSubdId);

        var selected = Subdistributors.FirstOrDefault(x => x.SubDistributorId == SelectedSubdId);

        if (selected != null)
        {
            SelectedSubdName = selected.SubdName;
            await OnSubdChanged.InvokeAsync(selected);
        }
    }

    void UpdateDisplay()
    {
        var selected = Subdistributors.FirstOrDefault(x => x.SubDistributorId == SelectedSubdId);
        SelectedSubdName = selected?.SubdName ?? "";
    }
}

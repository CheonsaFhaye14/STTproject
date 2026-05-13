using Microsoft.AspNetCore.Components;
using STTproject.Data;

namespace STTproject.Features.User.Home.Components.Sections
{
    public partial class SubdCard
    {
        [Parameter, EditorRequired] public SubDistributor Subdistributor { get; set; } = default!;
        [Parameter, EditorRequired] public EventCallback<int> OnInputInvoice { get; set; }

        private Task HandleInputInvoice()
        {
            return OnInputInvoice.InvokeAsync(Subdistributor.SubDistributorId);
        }
    }
}

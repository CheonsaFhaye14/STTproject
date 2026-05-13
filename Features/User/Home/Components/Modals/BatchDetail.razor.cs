namespace STTproject.Features.User.Home.Components.Modals
{
    public partial class BatchDetail
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public bool Show { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string BatchId { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public List<STTproject.Services.HomeSalesInvoiceBatchInvoiceRow> BatchInvoices { get; set; } = new();

        [Microsoft.AspNetCore.Components.Parameter]
        public Microsoft.AspNetCore.Components.EventCallback OnClose { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public Microsoft.AspNetCore.Components.EventCallback<int> OnViewInvoiceDetails { get; set; }

        private async Task HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Escape")
            {
                await OnClose.InvokeAsync();
            }
        }
    }
}

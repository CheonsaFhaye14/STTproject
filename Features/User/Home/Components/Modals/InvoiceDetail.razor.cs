namespace STTproject.Features.User.Home.Components.Modals
{
    public partial class InvoiceDetail
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public bool Show { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string InvoiceCode { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Components.Parameter]
        public STTproject.Services.HomeSalesInvoiceDetailRow? InvoiceDetails { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public string? ErrorMessage { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public Microsoft.AspNetCore.Components.EventCallback OnClose { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public Microsoft.AspNetCore.Components.EventCallback OnEdit { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public Microsoft.AspNetCore.Components.EventCallback OnDelete { get; set; }

        private async Task HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Escape")
            {
                await OnClose.InvokeAsync();
            }
        }
    }
}

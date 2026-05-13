using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using STTproject.Data;
using STTproject.Services;

namespace STTproject.Features.User.Home.Components.Pages
{
    public partial class Home
    {
        protected int userid;
        protected string UserFullName = string.Empty;

        protected List<SubDistributor> subdList = new();

        protected override async Task OnParametersSetAsync()
        {
            if (!userContext.UserId.HasValue)
            {
                Navigation.NavigateTo("/");
                return;
            }

            userid = userContext.UserId.Value;
            UserFullName = string.Empty;

            var user = await homeService.GetUserAsync(userid);
            if (user != null)
            {
                UserFullName = user.FullName;
            }

            subdList = await homeService.GetSubDistributorsAsync(userid);
        }

        async Task InputSalesInvoice(int subDistributorId)
        {
            // Force full page reload to ensure component lifecycle runs properly
            Navigation.NavigateTo($"/salesinvoice/{subDistributorId}", forceLoad: true);
        }

        async Task GoToMapItems()
        {
            Navigation.NavigateTo("/mapitem", forceLoad: true);
        }
    }
}

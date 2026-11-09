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

        protected bool isNavigating;
        protected string navigatingTitle = "Loading…";

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

        Task InputSalesInvoice(int subDistributorId) =>
            // Force full page reload to ensure component lifecycle runs properly
            NavigateWithLoadingAsync($"/salesinvoice/{subDistributorId}", "Opening sales invoice…");

        Task GoToMapItems() =>
            NavigateWithLoadingAsync("/mapitem", "Opening Map Items…");

        /// <summary>
        /// Shows the LoadingOverlay, then does a full page load.
        /// The overlay stays up until the browser leaves the page, and extra clicks are ignored.
        /// </summary>
        private async Task NavigateWithLoadingAsync(string url, string title)
        {
            if (isNavigating)
            {
                return;
            }

            isNavigating = true;
            navigatingTitle = title;
            StateHasChanged();
            await Task.Yield(); // let the overlay render before the page starts unloading

            Navigation.NavigateTo(url, forceLoad: true);
        }
    }
}
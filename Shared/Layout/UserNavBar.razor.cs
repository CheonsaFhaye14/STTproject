namespace STTproject.Shared.Layout
{
    public partial class UserNavBar
    {
        private string MapItemHref => "mapitem";

        private void Logout()
        {
            userContext.UserId = null;
            Navigation.NavigateTo("/", forceLoad: true);
        }
    }
}

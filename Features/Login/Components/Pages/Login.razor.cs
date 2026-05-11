namespace STTproject.Features.Login.Components.Pages
{
    public partial class Login
    {
        private string? username;
        private string? password;

        private void HandleLogin()
        {
            userContext.UserId = 11;
            Navigation.NavigateTo($"/home?uid={userContext.UserId.Value}");
        }
    }
}

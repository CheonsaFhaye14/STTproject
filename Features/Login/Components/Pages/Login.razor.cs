using Microsoft.AspNetCore.Components;

namespace STTproject.Features.Login.Components.Pages
{
    public partial class Login
    {
        [Parameter]
        [SupplyParameterFromQuery(Name = "error")]
        public string? ErrorCode { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "username")]
        public string? Username { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "rememberMe")]
        public string? RememberMeRaw { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "role")]
        public string? SelectedRole { get; set; }

        private string? loginErrorMessage;

        private bool RememberMe => string.Equals(RememberMeRaw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(RememberMeRaw, "1", StringComparison.OrdinalIgnoreCase);

        private bool IsAdminSelected => string.Equals(SelectedRole, "Admin", StringComparison.OrdinalIgnoreCase);

        private bool IsEncoderSelected => !IsAdminSelected;

        protected override void OnParametersSet()
        {
            loginErrorMessage = ErrorCode switch
            {
                "missing" => "Enter a username and password.",
                "invalid" => "Invalid username or password.",
                "role" => "Selected role does not match your account.",
                "db" => "Cannot connect to the database right now. Please try again.",
                _ => null
            };
        }
    }
}

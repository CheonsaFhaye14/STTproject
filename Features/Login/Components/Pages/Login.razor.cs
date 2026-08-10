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

        [Parameter]
        [SupplyParameterFromQuery(Name = "success")]
        public string? SuccessCode { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "returnUrl")]
        public string? ReturnUrl { get; set; }

        private string? loginErrorMessage;

        private bool ShowSuccessToast => string.Equals(SuccessCode, "true", StringComparison.OrdinalIgnoreCase);

        // Where the toast redirects to once it's shown. Defaults to /dashboard —
        // change this or pass ?returnUrl=/wherever from your /login handler.
        private string RedirectTarget => string.IsNullOrWhiteSpace(ReturnUrl) ? "/dashboard" : ReturnUrl;

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
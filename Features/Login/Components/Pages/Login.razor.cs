using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using STTproject.Features.Login.Services;

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

        // Available during the server-side (SSR / prerender) pass only.
        [CascadingParameter]
        private HttpContext? HttpContext { get; set; }

        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private ILoginService LoginService { get; set; } = default!;
        [Inject] private ILogger<Login> Logger { get; set; } = default!;

        private string? loginErrorMessage;

        private bool ShowSuccessToast => string.Equals(SuccessCode, "true", StringComparison.OrdinalIgnoreCase);

        // Where the toast redirects to once it's shown. Defaults to /dashboard —
        // change this or pass ?returnUrl=/wherever from your /login handler.
        private string RedirectTarget => string.IsNullOrWhiteSpace(ReturnUrl) ? "/dashboard" : ReturnUrl;

        private bool RememberMe => string.Equals(RememberMeRaw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(RememberMeRaw, "1", StringComparison.OrdinalIgnoreCase);

        private bool IsAdminSelected => string.Equals(SelectedRole, "Admin", StringComparison.OrdinalIgnoreCase);

        private bool IsEncoderSelected => !IsAdminSelected;

        protected override async Task OnInitializedAsync()
        {
            // Only the server-side pass can read the auth cookie / redirect.
            if (HttpContext?.User.Identity?.IsAuthenticated != true)
                return;

            // Came back from a failed login attempt, or the success toast is handling
            // its own redirect: show the page as-is.
            if (!string.IsNullOrEmpty(ErrorCode) || ShowSuccessToast)
                return;

            string? target;

            try
            {
                target = await ResolveLandingPageAsync(HttpContext);
            }
            catch (Exception ex)
            {
                // DB unreachable etc. — just show the login form instead of crashing.
                Logger.LogError(ex, "Could not verify the signed-in user on the login page.");
                return;
            }

            if (target is not null)
            {
                // During SSR this becomes an HTTP redirect.
                Nav.NavigateTo(target);
            }
        }

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

        /// <summary>
        /// Returns where an already signed-in user should go, or null (after signing them out)
        /// if their account no longer exists or was deactivated.
        /// </summary>
        private async Task<string?> ResolveLandingPageAsync(HttpContext httpContext)
        {
            var idClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

            var user = int.TryParse(idClaim, out var userId)
                ? await LoginService.GetActiveUserAsync(userId)
                : null;

            if (user is null)
            {
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return null;
            }

            return string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                ? "/admin"
                : "/home";
        }
    }
}
using Microsoft.AspNetCore.Components;
using STTproject.Features.User.Profile.Services;
using STTproject.Services;

namespace STTproject.Features.User.Profile.Components.Modal;

public partial class ChangePass
{
    [Inject]
    private IProfileService? ProfileService { get; set; }

    [Parameter]
    public int? UserId { get; set; }

    private bool ShowPasswordModal = false;

    private string? CurrentPassword;
    private string? NewPassword;
    private string? ConfirmPassword;
    private string? PasswordErrorMessage;
    private bool IsSubmitting = false;

    private async Task CloseModal()
    {
        ResetForm();
        ShowPasswordModal = false;
        await InvokeAsync(StateHasChanged);
    }

    private void OpenModal()
    {
        if (UserId.HasValue)
        {
            ShowPasswordModal = true;
        }
    }

    private async Task OnBackdropClick()
    {
        if (!IsSubmitting)
        {
            await CloseModal();
        }
    }

    private async Task ChangePasswordAsync()
    {
        try
        {
            PasswordErrorMessage = null;
            IsSubmitting = true;

            // Validation
            if (string.IsNullOrWhiteSpace(CurrentPassword))
            {
                PasswordErrorMessage = "Current password is required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                PasswordErrorMessage = "New password is required.";
                return;
            }

            if (NewPassword.Length < 6)
            {
                PasswordErrorMessage = "New password must be at least 6 characters long.";
                return;
            }

            if (NewPassword != ConfirmPassword)
            {
                PasswordErrorMessage = "New password and confirm password do not match.";
                return;
            }

            if (CurrentPassword == NewPassword)
            {
                PasswordErrorMessage = "New password must be different from current password.";
                return;
            }

            if (!UserId.HasValue || ProfileService == null)
            {
                PasswordErrorMessage = "User context not available.";
                return;
            }

            // Call the service to change password
            var success = await ProfileService.ChangePasswordAsync(
                UserId.Value,
                CurrentPassword,
                NewPassword
            );

            if (success)
            {
                ResetForm();
                ShowPasswordModal = false;
                PasswordErrorMessage = null;
            }
            else
            {
                PasswordErrorMessage = "Failed to change password. Current password may be incorrect.";
            }
        }
        catch (Exception ex)
        {
            PasswordErrorMessage = $"Error changing password: {ex.Message}";
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private void ResetForm()
    {
        CurrentPassword = null;
        NewPassword = null;
        ConfirmPassword = null;
        PasswordErrorMessage = null;
    }
}
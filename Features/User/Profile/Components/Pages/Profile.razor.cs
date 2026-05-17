using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Services;

namespace STTproject.Features.User.Profile.Components.Pages;

public partial class Profile
{
    [Inject]
    private IUserContextService UserContext { get; set; } = null!;

    [Inject]
    private IDbContextFactory<SttprojectContext> DbContextFactory { get; set; } = null!;

    private STTproject.Data.User? CurrentUser;
    private STTproject.Data.User? EditingUser;
    private string? ErrorMessage;
    private string? SuccessMessage;
    private bool IsLoading = true;
    private bool IsEditMode = false;
    private bool ShowPasswordModal = false;

    private string? CurrentPassword;
    private string? NewPassword;
    private string? ConfirmPassword;
    private string? PasswordErrorMessage;

    protected override async Task OnInitializedAsync()
    {
        await LoadUserAsync();
    }

    private async Task LoadUserAsync()
    {
        try
        {
            IsLoading = true;
            if (UserContext.UserId.HasValue)
            {
                using var context = await DbContextFactory.CreateDbContextAsync();
                CurrentUser = await context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == UserContext.UserId.Value);

                if (CurrentUser == null)
                {
                    ErrorMessage = "User not found.";
                }
                else
                {
                    EditingUser = new STTproject.Data.User
                    {
                        UserId = CurrentUser.UserId,
                        Username = CurrentUser.Username,
                        FullName = CurrentUser.FullName,
                        Email = CurrentUser.Email,
                        Role = CurrentUser.Role,
                        IsActive = CurrentUser.IsActive,
                        CreatedDate = CurrentUser.CreatedDate,
                        UpdatedDate = CurrentUser.UpdatedDate,
                        Password = CurrentUser.Password
                    };
                }
            }
            else
            {
                ErrorMessage = "User context not available.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading user profile: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void EnableEditMode()
    {
        IsEditMode = true;
        SuccessMessage = null;
    }

    private async Task SaveChangesAsync()
    {
        try
        {
            ErrorMessage = null;
            SuccessMessage = null;

            if (string.IsNullOrWhiteSpace(EditingUser?.FullName))
            {
                ErrorMessage = "Full Name cannot be empty.";
                return;
            }

            if (CurrentUser == null)
            {
                ErrorMessage = "User context not available.";
                return;
            }

            using var context = await DbContextFactory.CreateDbContextAsync();
            var user = await context.Users.FirstOrDefaultAsync(u => u.UserId == CurrentUser.UserId);

            if (user == null)
            {
                ErrorMessage = "User not found.";
                return;
            }

            user.FullName = EditingUser.FullName;
            user.Email = EditingUser.Email;
            user.UpdatedDate = DateTime.Now;

            await context.SaveChangesAsync();

            CurrentUser = new STTproject.Data.User
            {
                UserId = user.UserId,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                IsActive = user.IsActive,
                CreatedDate = user.CreatedDate,
                UpdatedDate = user.UpdatedDate,
                Password = user.Password
            };

            IsEditMode = false;
            SuccessMessage = "Profile updated successfully!";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error saving changes: {ex.Message}";
        }
    }

    private void CancelEdit()
    {
        IsEditMode = false;
        ErrorMessage = null;
        if (CurrentUser != null)
        {
            EditingUser = new STTproject.Data.User
            {
                UserId = CurrentUser.UserId,
                Username = CurrentUser.Username,
                FullName = CurrentUser.FullName,
                Email = CurrentUser.Email,
                Role = CurrentUser.Role,
                IsActive = CurrentUser.IsActive,
                CreatedDate = CurrentUser.CreatedDate,
                UpdatedDate = CurrentUser.UpdatedDate,
                Password = CurrentUser.Password
            };
        }
    }

    private void OpenPasswordModal()
    {
        ShowPasswordModal = true;
        CurrentPassword = null;
        NewPassword = null;
        ConfirmPassword = null;
        PasswordErrorMessage = null;
    }

    private async Task ChangePasswordAsync()
    {
        try
        {
            PasswordErrorMessage = null;

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

            if (CurrentUser == null)
            {
                PasswordErrorMessage = "User context not available.";
                return;
            }

            using var context = await DbContextFactory.CreateDbContextAsync();
            var user = await context.Users.FirstOrDefaultAsync(u => u.UserId == CurrentUser.UserId);

            if (user == null)
            {
                PasswordErrorMessage = "User not found.";
                return;
            }

            // Verify current password
            if (user.Password != CurrentPassword)
            {
                PasswordErrorMessage = "Current password is incorrect.";
                return;
            }

            user.Password = NewPassword;
            user.UpdatedDate = DateTime.Now;

            await context.SaveChangesAsync();

            ShowPasswordModal = false;
            SuccessMessage = "Password changed successfully!";
            CurrentPassword = null;
            NewPassword = null;
            ConfirmPassword = null;
        }
        catch (Exception ex)
        {
            PasswordErrorMessage = $"Error changing password: {ex.Message}";
        }
    }

    private void ClosePasswordModal()
    {
        ShowPasswordModal = false;
        CurrentPassword = null;
        NewPassword = null;
        ConfirmPassword = null;
        PasswordErrorMessage = null;
    }

    private string FormatDate(DateTime? date)
    {
        return date?.ToString("MMMM dd, yyyy HH:mm:ss") ?? "N/A";
    }

    private string GetStatusBadge(bool isActive)
    {
        return isActive ? "Active" : "Inactive";
    }

    private string GetStatusClass(bool isActive)
    {
        return isActive ? "badge bg-success" : "badge bg-danger";
    }
}

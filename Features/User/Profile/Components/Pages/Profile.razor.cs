using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using STTproject.Features.User.Profile.Services;
using STTproject.Services;
using STTproject.Features.User.Profile.DTOS;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.User.Profile.Components.Pages;


public partial class Profile
{
    private const string UserIdStateKey = "profile-user-id";

    [Inject]
    private IProfileService? ProfileService { get; set; }

    [Inject]
    private IDbContextFactory<SttprojectContext>? DbContextFactory { get; set; }

    [Inject]
    private IHttpContextAccessor? HttpContextAccessor { get; set; }

    [Inject]
    private PersistentComponentState? ApplicationState { get; set; }

    private STTproject.Data.User? CurrentUser;
    private STTproject.Data.User? EditingUser;
    private string? ErrorMessage;
    private string? SuccessMessage;
    private bool IsLoading = true;
    private bool IsEditMode = false;
    private int? UserId;
    private PersistingComponentStateSubscription? PersistingSubscription;

    protected override async Task OnInitializedAsync()
    {
        if (ApplicationState != null)
        {
            if (!ApplicationState.TryTakeFromJson<int?>(UserIdStateKey, out var restoredUserId))
            {
                restoredUserId = HttpContextAccessor?.HttpContext?.Request.Cookies.TryGetValue(UserContextService.UserIdCookieName, out var cookieValue) == true &&
                    int.TryParse(cookieValue, out var parsedUserId)
                    ? parsedUserId
                    : null;
            }

            UserId = restoredUserId;
            PersistingSubscription = ApplicationState.RegisterOnPersisting(PersistUserState);
        }

        await LoadUserAsync();
    }

    private Task PersistUserState()
    {
        ApplicationState?.PersistAsJson(UserIdStateKey, UserId);
        return Task.CompletedTask;
    }

    private async Task LoadUserAsync()
    {
        try
        {
            IsLoading = true;
            if (UserId.HasValue && ProfileService != null && DbContextFactory != null)
            {
                var userId = UserId.Value;
                var profileData = await ProfileService.GetProfileDataAsync(userId);

                if (profileData == null)
                {
                    ErrorMessage = "User not found.";
                }
                else
                {
                    using var context = await DbContextFactory.CreateDbContextAsync();
                    CurrentUser = await context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.UserId == userId);

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
                        await InvokeAsync(StateHasChanged);
                    }
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
            await InvokeAsync(StateHasChanged);
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

            if (CurrentUser == null || ProfileService == null)
            {
                ErrorMessage = "User context not available.";
                return;
            }

            var profileData = new ProfileData
            {
                Username = EditingUser!.Username,
                FullName = EditingUser.FullName,
                Email = EditingUser.Email ?? string.Empty,
                Role = EditingUser.Role,
                CreatedDate = EditingUser.CreatedDate,
                UpdatedDate = DateTime.UtcNow
            };

            var success = await ProfileService.UpdateProfileAsync(CurrentUser.UserId, profileData);

            if (success)
            {
                CurrentUser.Role = EditingUser.Role;
                CurrentUser.FullName = EditingUser.FullName;
                CurrentUser.Email = EditingUser.Email;
                CurrentUser.UpdatedDate = DateTime.UtcNow;

                IsEditMode = false;
                SuccessMessage = "Profile updated successfully!";
                
                // Clear success message after 3 seconds
                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    SuccessMessage = null;
                    InvokeAsync(StateHasChanged);
                });
            }
            else
            {
                ErrorMessage = "Failed to update profile.";
            }
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

    public void Dispose()
    {
        PersistingSubscription?.Dispose();
    }
}

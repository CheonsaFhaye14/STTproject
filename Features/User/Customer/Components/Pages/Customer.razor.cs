using Microsoft.AspNetCore.Components;
using STTproject.Features.User.Customer.DTOs;
using STTproject.Features.User.Customer.Services;
using STTproject.Services;

namespace STTproject.Features.User.Customer.Components.Pages;

public partial class Customer
{
    [Inject]
    private ICustomerService? CustomerService { get; set; }

    [Inject]
    private IUserContextService? UserContext { get; set; }

    private CustomerListResponseDto? CustomerData;
    private string? ErrorMessage;
    private bool IsLoading = true;

    protected override async Task OnInitializedAsync()
    {
        await LoadCustomersAsync();
    }

    private async Task LoadCustomersAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            if (UserContext?.UserId == null)
            {
                ErrorMessage = "User context not available.";
                return;
            }

            if (CustomerService == null)
            {
                ErrorMessage = "Customer service not available.";
                return;
            }

            CustomerData = await CustomerService.GetCustomersWithBranchesAsync(UserContext.UserId.Value);

            if (CustomerData == null)
            {
                ErrorMessage = "Could not load customer data.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading customers: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}

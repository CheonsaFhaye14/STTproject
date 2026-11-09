using STTproject.Data;
using STTproject.Features.Admin.Customers.Services;

namespace STTproject.Features.Admin.Customers.Validators;

public static class CustomerValidations
{
    public static class AddCustomer
    {
        public static readonly CustomerField subdistributor = new(nameof(subdistributor), "Subdistributor", true, "Subdistributor is required.");
        public static readonly CustomerField customercode = new(nameof(customercode), "Customer Code", true, "Customer code is required.");
        public static readonly CustomerField customername = new(nameof(customername), "Customer Name", true, "Customer name is required.");
    }

    public static string Label(CustomerField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    // excludeCustomerId: the customer being edited, so its own code doesn't count as a duplicate.
    public static async Task<Dictionary<string, string>> ValidateAddCustomerAsync(
        Customer customer, IAdminCustomerService service, int? excludeCustomerId = null)
    {
        var errors = new Dictionary<string, string>();
        if (customer.SubDistributorId <= 0)
        {
            errors[AddCustomer.subdistributor.Key] = AddCustomer.subdistributor.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(customer.CustomerCode))
        {
            errors[AddCustomer.customercode.Key] = AddCustomer.customercode.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(customer.CustomerName))
        {
            errors[AddCustomer.customername.Key] = AddCustomer.customername.ErrorMessage;
        }

        if (!string.IsNullOrWhiteSpace(customer.CustomerCode) && customer.SubDistributorId > 0)
        {
            if (await service.CustomerCodeExistsAsync(customer.CustomerCode, customer.SubDistributorId, excludeCustomerId))
            {
                errors[AddCustomer.customercode.Key] = "This Customer Code already exists for the selected Subdistributor.";
            }
        }
        return errors;
    }
}


public sealed record CustomerField(string Key, string Label, bool Required, string ErrorMessage);
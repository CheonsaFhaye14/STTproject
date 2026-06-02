using STTproject.Data;

namespace STTproject.Features.Admin.Customers.Validators;

public static class CustomerValidations
{
    public static class AddCustomer
    {
        public static readonly CustomerField subdistributor = new(nameof(subdistributor), "Subdistributor", true, "Subdistributor is required.");
        public static readonly CustomerField customercode = new(nameof(customercode), "Customer Code", true, "Customer code is required.");
        public static readonly CustomerField customername = new(nameof(customername), "Customer Name", true, "Customer name is required.");
        public static readonly CustomerField customertype = new(nameof(customertype), "Customer Type", true, "Customer type is required.");
    }

    public static string Label(CustomerField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateAddCustomerAsync(
        Customer customer,
        Func<Task<bool>> customerCodeExistsAsync)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(customer.CustomerCode))
        {
            errors[AddCustomer.customercode.Key] = AddCustomer.customercode.ErrorMessage;
        }
        else if (await customerCodeExistsAsync())
        {
            errors[AddCustomer.customercode.Key] = "Customer code already exists on the same subdistributor.";
        }

        if (string.IsNullOrWhiteSpace(customer.CustomerName))
        {
            errors[AddCustomer.customername.Key] = AddCustomer.customername.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(customer.CustomerType))
        {
            errors[AddCustomer.customertype.Key] = AddCustomer.customertype.ErrorMessage;
        }
        return errors;
    }
}


public sealed record CustomerField(string Key, string Label, bool Required, string ErrorMessage);

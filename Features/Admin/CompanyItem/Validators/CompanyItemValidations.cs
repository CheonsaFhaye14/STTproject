using STTproject.Data;

namespace STTproject.Features.Admin.CompanyItem.Validators;

public static class CompanyItemValidations
{
    public static class AddCompanyItem
    {
        public static readonly CompanyItemField ItemCode = new(nameof(ItemCode), "Item Code", true, "Item code is required.");
        public static readonly CompanyItemField ItemName = new(nameof(ItemName), "Item Name", true, "Item name is required.");
        public static readonly CompanyItemField Principal = new(nameof(Principal), "Principal", true, "Principal is required.");
        public static readonly CompanyItemField Category = new(nameof(Category), "Category", true, "Category is required.");
    }

    public static string Label(CompanyItemField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateAddCompanyItemAsync(
        Data.CompanyItem companyItem
    )
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(companyItem.ItemCode))
        {
            errors[AddCompanyItem.ItemCode.Key] = AddCompanyItem.ItemCode.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(companyItem.ItemName))
        {
            errors[AddCompanyItem.ItemName.Key] = AddCompanyItem.ItemName.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(companyItem.Principal))
        {
            errors[AddCompanyItem.Principal.Key] = AddCompanyItem.Principal.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(companyItem.Category))
        {
            errors[AddCompanyItem.Category.Key] = AddCompanyItem.Category.ErrorMessage;
        }
        return errors;
    }
}


public sealed record CompanyItemField(string Key, string Label, bool Required, string ErrorMessage);

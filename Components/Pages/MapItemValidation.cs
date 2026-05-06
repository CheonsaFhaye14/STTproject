namespace STTproject.Components.Pages.Validation;

public static class MapItemValidation
{
    public static class Form
    {
        public static readonly MapItemField SkuCode = new(nameof(SkuCode), "SKU Code", true, "SKU code is required.");
        public static readonly MapItemField ItemName = new(nameof(ItemName), "Item Name", true, "Item name is required.");
        public static readonly MapItemField CompanyItem = new(nameof(CompanyItem), "Company Item", true, "Company item is required.");
        public static readonly MapItemField UnitOfMeasure = new(nameof(UnitOfMeasure), "Unit of Measure", true, "Unit of measure is required.");
    }

    public static string Label(MapItemField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateFormAsync(
        string? skuCode,
        string? itemName,
        int? selectedCompanyItemId,
        bool hasAnyUom,
        Func<Task<bool>> skuExistsAsync)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(skuCode))
        {
            errors[Form.SkuCode.Key] = Form.SkuCode.ErrorMessage;
        }
        else if (await skuExistsAsync())
        {
            errors[Form.SkuCode.Key] = "SKU code already exists for this sub distributor.";
        }

        if (string.IsNullOrWhiteSpace(itemName))
        {
            errors[Form.ItemName.Key] = Form.ItemName.ErrorMessage;
        }

        if (!selectedCompanyItemId.HasValue || selectedCompanyItemId.Value <= 0)
        {
            errors[Form.CompanyItem.Key] = Form.CompanyItem.ErrorMessage;
        }

        if (!hasAnyUom)
        {
            errors[Form.UnitOfMeasure.Key] = Form.UnitOfMeasure.ErrorMessage;
        }

        return errors;
    }
}

public sealed record MapItemField(string Key, string Label, bool Required, string ErrorMessage);
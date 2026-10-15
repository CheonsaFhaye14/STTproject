using STTproject.Features.Admin.CompanyItem.Services;

namespace STTproject.Features.Admin.CompanyItem.Validators;

public static class CompanyItemValidations
{
    public static class AddCompanyItem
    {
        public static readonly CompanyItemField ItemCode = new(nameof(ItemCode), "Item Code", true, "Item code is required.");
        public static readonly CompanyItemField ItemName = new(nameof(ItemName), "Item Name", true, "Item name is required.");
        public static readonly CompanyItemField Principal = new(nameof(Principal), "Principal", true, "Principal is required.");
        public static readonly CompanyItemField StockUomName = new(nameof(StockUomName), "Stock UOM", true, "Stock UOM is required.");
        public static readonly CompanyItemField StockPrice =
            new(nameof(StockPrice), "Stock Price", true, "Stock Price is required.");
        public static readonly CompanyItemField StockPriceRegional =
            new(nameof(StockPriceRegional), "Regional Stock Price", true, "Regional Stock Price is required.");
        public static readonly CompanyItemField StockPriceNational =
            new(nameof(StockPriceNational), "National Stock Price", true, "National Stock Price is required.");
    }

    public static string Label(CompanyItemField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateAddCompanyItemAsync(Data.CompanyItem companyItem, IAdminCompanyItemService service, int? excludeId = null, bool isSingleStockPrice = true,
        decimal? stockPrice = null, decimal? stockPriceRegional = null, decimal? stockPriceNational = null)
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

            if (!string.IsNullOrWhiteSpace(companyItem.ItemCode) &&
                !string.IsNullOrWhiteSpace(companyItem.ItemName) &&
                await service.CompanyItemExistsAsync(companyItem.ItemCode, companyItem.ItemName, excludeId))
            {
                errors[AddCompanyItem.ItemCode.Key] = "This Item Code and Item Name combination already exists.";
                errors[AddCompanyItem.ItemName.Key] = "This Item Code and Item Name combination already exists.";
            }
            if (!string.IsNullOrWhiteSpace(companyItem.ItemCode) &&
                await service.ItemCodeExistsAsync(companyItem.ItemCode, excludeId))
            {
                errors[AddCompanyItem.ItemCode.Key] = "This Item Code already exists.";
            }

            if (string.IsNullOrWhiteSpace(companyItem.Principal))
            {
                errors[AddCompanyItem.Principal.Key] = AddCompanyItem.Principal.ErrorMessage;
            }
            if (string.IsNullOrWhiteSpace(companyItem.StockUomName))
            {
                errors[AddCompanyItem.StockUomName.Key] = AddCompanyItem.StockUomName.ErrorMessage;
            }  
            if (isSingleStockPrice)
            {
                if (!stockPrice.HasValue || stockPrice.Value <= 0)
                {
                    errors[AddCompanyItem.StockPrice.Key] =
                        "Stock Price must be greater than zero.";
                }
            }
            else
            {
                if (!stockPriceRegional.HasValue || stockPriceRegional.Value <= 0)
                {
                    errors[AddCompanyItem.StockPriceRegional.Key] =
                        "Regional Stock Price must be greater than zero.";
                }

                if (!stockPriceNational.HasValue || stockPriceNational.Value <= 0)
                {
                    errors[AddCompanyItem.StockPriceNational.Key] =
                        "National Stock Price must be greater than zero.";
                }
            }
            return errors;
        }

}

public sealed record CompanyItemField(string Key, string Label, bool Required, string ErrorMessage);
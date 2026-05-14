using STTproject.Models;
using STTproject.Data;
namespace STTproject.Features.User.SalesInvoice.Validators;

public static class SalesInvoiceValidation
{
    public static class Header
    {
        public static readonly SalesInvoiceField InvoiceNumber = new(nameof(InvoiceNumber), "Sales Invoice Code", true, "Sales Invoice Code is required.");
        public static readonly SalesInvoiceField InvoiceDate = new(nameof(InvoiceDate), "Sales Invoice Date", true, "Invoice date is required.");
        public static readonly SalesInvoiceField OrderType = new(nameof(OrderType), "Order Type", true, "Order type is required.");
        public static readonly SalesInvoiceField CustomerCode = new(nameof(CustomerCode), "Customer Code", true, "Customer code is required.");
        public static readonly SalesInvoiceField CustomerName = new(nameof(CustomerName), "Customer Name", true, "Customer name is required.");
        public static readonly SalesInvoiceField CustomerBranch = new(nameof(CustomerBranch), "Customer Branch", true, "Customer branch is required.");
    }

    public static class AddItem
    {
        public static readonly SalesInvoiceField Sku = new(nameof(Sku), "SKU", true, "SKU is required.");
        public const string InvalidSkuErrorMessage = "SKU Code does not exist.";
        public static readonly SalesInvoiceField ItemName = new(nameof(ItemName), "Item Name", true, "Item name is required.");
        public static readonly SalesInvoiceField Uom = new(nameof(Uom), "Unit of Measure", true, "Unit of measure is required.");
        public static readonly SalesInvoiceField Quantity = new(nameof(Quantity), "Quantity", true, "Quantity is required.");
    }

    public static class EditItem
    {
        public static readonly SalesInvoiceField ItemName = new(nameof(ItemName), "Item Name", true, "Item name is required.");
        public static readonly SalesInvoiceField Uom = new(nameof(Uom), "Unit of Measure", true, "Unit of measure is required.");
        public static readonly SalesInvoiceField Quantity = new(nameof(Quantity), "Quantity", true, "Quantity is required.");
    }

    public static string Label(SalesInvoiceField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateHeaderAsync(
        InputInvoiceModel invoice,
        bool hasCustomerBranches,
        Func<Task<bool>> invoiceNumberExistsAsync)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            errors[Header.InvoiceNumber.Key] = Header.InvoiceNumber.ErrorMessage;
        }
        else if (await invoiceNumberExistsAsync())
        {
            errors[Header.InvoiceNumber.Key] = "Sales invoice code already exists.";
        }

        if (invoice.InvoiceDate == default)
        {
            errors[Header.InvoiceDate.Key] = Header.InvoiceDate.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(invoice.OrderType))
        {
            errors[Header.OrderType.Key] = Header.OrderType.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(invoice.CustomerCode) || invoice.CustomerId <= 0)
        {
            errors[Header.CustomerCode.Key] = Header.CustomerCode.ErrorMessage;
            errors[Header.CustomerName.Key] = Header.CustomerName.ErrorMessage;
        }

        if (hasCustomerBranches && invoice.CustomerBranchId <= 0)
        {
            errors[Header.CustomerBranch.Key] = Header.CustomerBranch.ErrorMessage;
        }

        return errors;
    }

    public static Dictionary<string, string> ValidateAddItemDraft(
        InputItemModel item,
        SubdItem? currentSubdItem,
        ItemsUom? currentUom)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(item.ItemCode))
        {
            errors[AddItem.Sku.Key] = AddItem.Sku.ErrorMessage;
        }
        else if (currentSubdItem is null)
        {
            errors[AddItem.Sku.Key] = AddItem.InvalidSkuErrorMessage;
        }

        if (currentSubdItem is null || string.IsNullOrWhiteSpace(item.ItemName))
        {
            errors[AddItem.ItemName.Key] = AddItem.ItemName.ErrorMessage;
        }

        if (currentUom is null || item.ItemsUomId <= 0)
        {
            errors[AddItem.Uom.Key] = AddItem.Uom.ErrorMessage;
        }

        if (item.Quantity <= 0)
        {
            errors[AddItem.Quantity.Key] = AddItem.Quantity.ErrorMessage;
        }

        return errors;
    }

    public static Dictionary<string, string> ValidateEditItemDraft(InputItemModel? item, bool hasSelection)
    {
        var errors = new Dictionary<string, string>();

        if (!hasSelection || item is null)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(item.ItemName))
        {
            errors[EditItem.ItemName.Key] = EditItem.ItemName.ErrorMessage;
        }

        if (item.ItemsUomId <= 0)
        {
            errors[EditItem.Uom.Key] = EditItem.Uom.ErrorMessage;
        }

        if (item.Quantity <= 0)
        {
            errors[EditItem.Quantity.Key] = EditItem.Quantity.ErrorMessage;
        }

        return errors;
    }
}

public sealed record SalesInvoiceField(string Key, string Label, bool Required, string ErrorMessage);

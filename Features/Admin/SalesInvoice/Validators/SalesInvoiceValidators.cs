using STTproject.Features.Admin.SalesInvoice.DTOs;

namespace STTproject.Features.Admin.SalesInvoice.Validators;

public static class SalesInvoiceValidator
{
    // ─── Field keys ─────────────────────────────────────────────────────────

    public static class Fields
    {
        public const string InvoiceCode     = "invoiceCode";
        public const string InvoiceDate     = "invoiceDate";
        public const string Customer        = "customerId";
        public const string SubDistributor  = "subDistributorId";
        public const string OrderType       = "orderType";
        public const string Items           = "items";
    }

    public static readonly string[] ValidOrderTypes = { "Regular", "Return", "Consignment" };

    // ─── Create ─────────────────────────────────────────────────────────────

    public static Dictionary<string, string> ValidateCreate(CreateSalesInvoiceDto dto)
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ValidateHeader(dto.SalesInvoiceCode, dto.SalesInvoiceDate, dto.CustomerId, dto.SubDistributorId, dto.OrderType, errors);
        ValidateItems(dto.Items.Select(i => (i.SubdItemId, i.ItemsUomId, i.Quantity, i.Amount)).ToList(), errors);
        return errors;
    }

    // ─── Update ─────────────────────────────────────────────────────────────

    public static Dictionary<string, string> ValidateUpdate(UpdateSalesInvoiceDto dto)
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (dto.SalesInvoiceId <= 0)
            errors[Fields.InvoiceCode] = "Invalid sales invoice.";

        ValidateHeader(dto.SalesInvoiceCode, dto.SalesInvoiceDate, dto.CustomerId, dto.SubDistributorId, dto.OrderType, errors);
        ValidateItems(dto.Items.Select(i => (i.SubdItemId, i.ItemsUomId, i.Quantity, i.Amount)).ToList(), errors);
        return errors;
    }

    // ─── Shared header validation ────────────────────────────────────────────

    private static void ValidateHeader(
        string invoiceCode,
        DateOnly invoiceDate,
        int customerId,
        int subDistributorId,
        string orderType,
        Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(invoiceCode))
            errors[Fields.InvoiceCode] = "Invoice code is required.";
        else if (invoiceCode.Trim().Length > 50)
            errors[Fields.InvoiceCode] = "Invoice code must not exceed 50 characters.";

        if (invoiceDate == default)
            errors[Fields.InvoiceDate] = "Invoice date is required.";
        else if (invoiceDate > DateOnly.FromDateTime(DateTime.Today))
            errors[Fields.InvoiceDate] = "Invoice date cannot be in the future.";

        if (customerId <= 0)
            errors[Fields.Customer] = "Customer is required.";

        if (subDistributorId <= 0)
            errors[Fields.SubDistributor] = "Sub distributor is required.";

        if (string.IsNullOrWhiteSpace(orderType))
            errors[Fields.OrderType] = "Order type is required.";
        else if (!ValidOrderTypes.Contains(orderType.Trim(), StringComparer.OrdinalIgnoreCase))
            errors[Fields.OrderType] = $"Order type must be one of: {string.Join(", ", ValidOrderTypes)}.";
    }

    // ─── Shared items validation ─────────────────────────────────────────────

    private static void ValidateItems(
        List<(int SubdItemId, int ItemsUomId, int Quantity, decimal Amount)> items,
        Dictionary<string, string> errors)
    {
        if (items.Count == 0)
        {
            errors[Fields.Items] = "At least one item is required.";
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var prefix = $"items[{i}]";

            if (item.SubdItemId <= 0)
                errors[$"{prefix}.subdItemId"] = "Item is required.";

            if (item.ItemsUomId <= 0)
                errors[$"{prefix}.itemsUomId"] = "Unit of measure is required.";

            if (item.Quantity <= 0)
                errors[$"{prefix}.quantity"] = "Quantity must be greater than zero.";

            if (item.Amount < 0)
                errors[$"{prefix}.amount"] = "Amount cannot be negative.";
        }

        // Duplicate SubdItemId + UOM combination check
        var duplicates = items
            .GroupBy(x => new { x.SubdItemId, x.ItemsUomId })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
            errors[Fields.Items] = "Duplicate item and UOM combinations are not allowed.";
    }

    // ─── Single field helpers (for inline validation in UI) ──────────────────

    public static string? ValidateInvoiceCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Invoice code is required.";
        if (value.Trim().Length > 50) return "Invoice code must not exceed 50 characters.";
        return null;
    }

    public static string? ValidateInvoiceDate(DateOnly? value)
    {
        if (value is null || value == default) return "Invoice date is required.";
        if (value > DateOnly.FromDateTime(DateTime.Today)) return "Invoice date cannot be in the future.";
        return null;
    }

    public static string? ValidateCustomer(int? value)
        => value is null or <= 0 ? "Customer is required." : null;

    public static string? ValidateSubDistributor(int? value)
        => value is null or <= 0 ? "Sub distributor is required." : null;

    public static string? ValidateOrderType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Order type is required.";
        if (!ValidOrderTypes.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            return $"Order type must be one of: {string.Join(", ", ValidOrderTypes)}.";
        return null;
    }

    public static string? ValidateQuantity(int? value)
        => value is null or <= 0 ? "Quantity must be greater than zero." : null;

    public static string? ValidateAmount(decimal? value)
        => value is null or < 0 ? "Amount cannot be negative." : null;
}
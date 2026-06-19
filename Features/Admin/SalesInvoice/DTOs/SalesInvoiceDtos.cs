namespace STTproject.Features.Admin.SalesInvoice.DTOs;

// ─── View / List ────────────────────────────────────────────────────────────

public sealed class SalesInvoiceListRow
{
    public int SalesInvoiceId { get; init; }
    public string SalesInvoiceCode { get; init; } = string.Empty;
    public DateOnly SalesInvoiceDate { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerCode { get; init; } = string.Empty;
    public string SubdName { get; init; } = string.Empty;
    public string OrderType { get; init; } = string.Empty;
    public string? SalesMan { get; init; }
    public decimal TotalAmount { get; init; }
    public int TotalItems { get; init; }
    public DateTime CreatedDate { get; init; }
    public DateTime? UpdatedDate { get; init; }
    public string? CreatedByName { get; init; }
    public string? UpdatedByName { get; init; }
}

public sealed class SalesInvoiceDetailDto
{
    public int SalesInvoiceId { get; init; }
    public string SalesInvoiceCode { get; init; } = string.Empty;
    public DateOnly SalesInvoiceDate { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerCode { get; init; } = string.Empty;
    public int SubDistributorId { get; init; }
    public string SubdName { get; init; } = string.Empty;
    public string OrderType { get; init; } = string.Empty;
    public string? SalesMan { get; init; }
    public DateTime CreatedDate { get; init; }
    public DateTime? UpdatedDate { get; init; }
    public string? CreatedByName { get; init; }
    public string? UpdatedByName { get; init; }
    public List<SalesInvoiceItemDto> Items { get; init; } = new();
    public decimal TotalAmount => Items.Sum(i => i.Amount);
}

public sealed class SalesInvoiceItemDto
{
    public int SalesInvoiceItemId { get; init; }
    public int SubdItemId { get; init; }
    public string SubdItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public int ItemsUomId { get; init; }
    public string UomName { get; init; } = string.Empty;
    public decimal UomPrice { get; init; }
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
    public decimal UnitPrice { get; init; }
}

// ─── Add ────────────────────────────────────────────────────────────────────

public sealed class CreateSalesInvoiceDto
{
    public string SalesInvoiceCode { get; set; } = string.Empty;
    public DateOnly SalesInvoiceDate { get; set; }
    public int CustomerId { get; set; }
    public int SubDistributorId { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public string? SalesMan { get; set; }
    public List<CreateSalesInvoiceItemDto> Items { get; set; } = new();
}

public sealed class CreateSalesInvoiceItemDto
{
    public int SubdItemId { get; set; }
    public int ItemsUomId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}

// ─── Edit ───────────────────────────────────────────────────────────────────

public sealed class UpdateSalesInvoiceDto
{
    public int SalesInvoiceId { get; set; }
    public string SalesInvoiceCode { get; set; } = string.Empty;
    public DateOnly SalesInvoiceDate { get; set; }
    public int CustomerId { get; set; }
    public int SubDistributorId { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public string? SalesMan { get; set; }
    public List<UpdateSalesInvoiceItemDto> Items { get; set; } = new();
}

public sealed class UpdateSalesInvoiceItemDto
{
    /// <summary>0 for new items, existing ID for items to update.</summary>
    public int SalesInvoiceItemId { get; set; }
    public int SubdItemId { get; set; }
    public int ItemsUomId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}

// ─── Dropdown helpers ────────────────────────────────────────────────────────

public sealed class SalesInvoiceCustomerDropdownItem
{
    public int CustomerId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
}

public sealed class SalesInvoiceSubdItemDropdownItem
{
    public int SubdItemId { get; init; }
    public string SubdItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public List<SalesInvoiceUomOption> Uoms { get; init; } = new();
}

public sealed class SalesInvoiceUomOption
{
    public int ItemsUomId { get; init; }
    public string UomName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal ConversionToBase { get; init; }
}
public sealed class SalesInvoiceItemDropdownItem
{
    public int SubdItemId { get; set; }
    public string SubdItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
}

// ─── Operation results ───────────────────────────────────────────────────────

public sealed class SalesInvoiceResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public int? SalesInvoiceId { get; init; }

    public static SalesInvoiceResult Success(int id) => new() { IsSuccess = true, SalesInvoiceId = id };
    public static SalesInvoiceResult NotFound() => new() { IsSuccess = false, ErrorMessage = "Sales invoice not found." };
    public static SalesInvoiceResult Duplicate(string msg) => new() { IsSuccess = false, ErrorMessage = msg };
    public static SalesInvoiceResult Failed(string msg) => new() { IsSuccess = false, ErrorMessage = msg };
}

public sealed class DeleteSalesInvoiceResult
{
    public bool IsDeleted { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteSalesInvoiceResult Success() => new() { IsDeleted = true };
    public static DeleteSalesInvoiceResult NotFound() => new() { IsDeleted = false, ErrorMessage = "Sales invoice not found." };
    public static DeleteSalesInvoiceResult Failed(string msg) => new() { IsDeleted = false, ErrorMessage = msg };
}
// Add to SalesInvoiceDTO.cs:
public sealed class SalesInvoiceSubDistributorDropdownItem
{
    public int SubDistributorId { get; init; }
    public string? SubdName { get; init; }
}
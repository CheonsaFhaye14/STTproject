


namespace STTproject.Features.Admin.ImportTemplate.Services;

public enum ImportFieldType { Text, Integer, Decimal, Date }
public enum ImportScope { Subd, Principal }

public sealed record ImportField(string Key, string Display, ImportFieldType Type, string? Note = null)
{
    private static readonly string[] TextModes =
        { "Text", "StripLeadingCode", "StripParenthetical", "ExtractBracketed", "Regex" };

    public IReadOnlyList<string> ReadModes => Type switch
    {
        ImportFieldType.Text    => TextModes,
        ImportFieldType.Integer => new[] { "Integer" },
        ImportFieldType.Decimal => new[] { "Decimal" },
        ImportFieldType.Date    => new[] { "Date" },
        _ => TextModes
    };
}

/// <summary>RequiredGroups: at least one key in each group must be mapped.</summary>
public sealed record ImportTypeDefinition(
    string Type,
    string Display,
    ImportScope Scope,  
    IReadOnlyList<ImportField> Fields,
    IReadOnlyList<string[]> RequiredGroups);
public static class ImportFieldRegistry
{
    private const ImportFieldType T = ImportFieldType.Text;
    private const ImportFieldType I = ImportFieldType.Integer;
    private const ImportFieldType D = ImportFieldType.Decimal;
    private const ImportFieldType Dt = ImportFieldType.Date;

    public static readonly IReadOnlyList<ImportTypeDefinition> All = new[]
    {
        new ImportTypeDefinition("SalesInvoice", "Sales Invoice", ImportScope.Subd,
            new ImportField[]
            {
                new("InvoiceCode",      "Invoice No.",        T),
                new("InvoiceDate",      "Invoice Date",       Dt),
                new("SalesManName",     "Salesman",           T),
                new("CustomerCode",     "Customer Code",      T),
                new("CustomerName",     "Customer Name",      T),
                new("CustomerType",     "Customer Type",      T),
                new("AddressLine",      "Address Line",       T),
                new("CityMunicipality", "City / Municipality",T),
                new("Province",         "Province",           T),
                new("SkuCode",          "SKU Code",           T),
                new("ItemName",         "Item Name",          T),
                new("Quantity",         "Quantity",           I, "Use together with Unit of Measure."),
                new("UnitOfMeasure",    "Unit of Measure",    T, "Only needed with Quantity."),
                new("CaseQuantity",     "Case Quantity",      I, "Split column: UOM is implied (case)."),
                new("DozenQuantity",    "Dozen Quantity",     I, "Split column: UOM is implied (dozen)."),
                new("PieceQuantity",    "Piece Quantity",     I, "Split column: UOM is implied (piece)."),
                new("InBoxQuantity",    "In-Box Quantity",    I, "Split column: UOM is implied (inbox)."),
                new("OrderType",        "Order Type",         T, "Invoice or Credit. Inferred if blank."),
                new("NetAmount",        "Net Amount",         D, "Used to infer Invoice/Credit from its sign."),
                new("FreeItems",        "Free Items",         T),
            },
            new[]
            {
                new[] { "InvoiceCode" },
                new[] { "InvoiceDate" },
                new[] { "SalesManName" },
                new[] { "CustomerCode", "CustomerName" },
                new[] { "SkuCode", "ItemName" },
                new[] { "Quantity", "CaseQuantity", "DozenQuantity", "PieceQuantity", "InBoxQuantity" },
            }),

        new ImportTypeDefinition("Customer", "Customer", ImportScope.Subd,
            new ImportField[]
            {
                new("CustomerCode", "Customer Code",   T),
                new("CustomerName", "Customer Name",   T),
                new("Province",     "Province",        T),
                new("City",         "City",            T),
                new("SubdCustCode", "Subd Cust Code",  T),
                new("SubdCustName", "Subd Cust Name",  T),
                new("AddressLine",  "Address Line",    T),
                new("ZipCode",      "Zip Code",        I),
                new("CustomerType", "Customer Type",   T),
            },
            new[]
            {
                new[] { "CustomerCode" }, new[] { "CustomerName" },
                new[] { "Province" },     new[] { "City" },
            }),

        new ImportTypeDefinition("CompanyItem", "Company Item", ImportScope.Principal,
            new ImportField[]
            {
                new("CompanyItemCode", "Company Item Code", T),
                new("CompanyItemName", "Company Item Name", T),
                new("Category",        "Category",          T),
                new("Principal",       "Principal",         T, "Required only when no principal is picked on the import screen."),
                new("Price",           "Stock Price",       D),
            },
            new[] { new[] { "CompanyItemCode" }, new[] { "CompanyItemName" } }),

        new ImportTypeDefinition("MapItem", "Map Item", ImportScope.Subd,
            new ImportField[]
            {
                new("SubDistributorCode", "SubDistributor Code", T),
                new("Principal",          "Principal",           T),
                new("CompanyItemCode",    "Company Item Code",   T),
                new("CompanyItemName",    "Company Item Name",   T),
                new("SubdItemCode",       "Subd Item Code",      T),
                new("SubdItemName",       "Subd Item Name",      T),
                new("UOM",                "UOM",                 T),
                new("Conversion",         "Conversion",          I),
                new("ConversionBasedOn",  "Conversion Based On", T, "Optional. Names the UOM this conversion is relative to."),
                new("Price",              "Price",               D),
            },
            new[]
            {
                new[] { "SubDistributorCode" }, new[] { "Principal" },
                new[] { "CompanyItemCode" },    new[] { "CompanyItemName" },
                new[] { "SubdItemCode" },       new[] { "SubdItemName" },
                new[] { "UOM" }, new[] { "Conversion" }, new[] { "Price" },
            }),
    };

    public static readonly IReadOnlyList<string> AllReadModes = new[]
    {
        "Text", "Integer", "Decimal", "Date",
        "StripLeadingCode", "StripParenthetical", "ExtractBracketed", "Regex"
    };

    public static ImportTypeDefinition? Get(string importType) =>
        All.FirstOrDefault(d => d.Type == importType);

    public static ImportField? GetField(string importType, string? fieldKey) =>
        fieldKey is null ? null : Get(importType)?.Fields.FirstOrDefault(f => f.Key == fieldKey);

    public static bool IsValidField(string importType, string? fieldKey) =>
        GetField(importType, fieldKey) is not null;

    public static bool IsValidReadMode(string importType, string fieldKey, string readMode) =>
        GetField(importType, fieldKey)?.ReadModes.Contains(readMode) == true;

    /// <summary>Returns a message for each required group the template doesn't cover.</summary>
    public static List<string> MissingRequired(string importType, IEnumerable<string> mappedKeys)
    {
        var def = Get(importType);
        if (def is null) return new() { $"Unknown import type '{importType}'." };

        var mapped = new HashSet<string>(mappedKeys, StringComparer.OrdinalIgnoreCase);
        return def.RequiredGroups
            .Where(g => !g.Any(mapped.Contains))
            .Select(g => g.Length == 1
                ? $"{DisplayOf(def, g[0])} is required."
                : $"One of {string.Join(" / ", g.Select(k => DisplayOf(def, k)))} is required.")
            .ToList();
    }

    private static string DisplayOf(ImportTypeDefinition def, string key) =>
        def.Fields.First(f => f.Key == key).Display;
}
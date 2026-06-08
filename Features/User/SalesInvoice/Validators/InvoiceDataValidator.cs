
using STTproject.Features.User.SalesInvoice.DTOs;

namespace STTproject.Features.User.SalesInvoice.Services;

public sealed class InvoiceDataValidator
{
    public static (bool IsValid, string ErrorMessage) ValidateRequiredHeaders(
        IReadOnlyDictionary<string, int> headers)
    {
        var missing = new List<string>();

        if (!headers.ContainsKey("InvoiceCode"))
            missing.Add("InvoiceCode");

        if (!headers.ContainsKey("InvoiceDate"))
            missing.Add("InvoiceDate");

        if (!headers.ContainsKey("SalesManName"))
            missing.Add("SalesManName");

        if (!headers.ContainsKey("CustomerCode") &&
            !headers.ContainsKey("CustomerName"))
        {
            missing.Add("(CustomerCode OR CustomerName)");
        }

        if (!headers.ContainsKey("SkuCode") &&
            !headers.ContainsKey("ItemName"))
        {
            missing.Add("(SkuCode OR ItemName)");
        }

        var hasSplitQuantities =
            headers.ContainsKey("CaseQuantity") ||
            headers.ContainsKey("PieceQuantity") ||
            headers.ContainsKey("DozenQuantity") ||
            headers.ContainsKey("InBoxQuantity");

        var hasSimpleQuantity = headers.ContainsKey("Quantity");
        var hasUom = headers.ContainsKey("UnitOfMeasure");

        if (!hasSplitQuantities && !hasSimpleQuantity)
        {
            missing.Add("(CaseQuantity OR PieceQuantity OR DozenQuantity OR InBoxQuantity OR Quantity)");
        }

        if (!hasSplitQuantities && hasSimpleQuantity && !hasUom)
        {
            missing.Add("UOM (required when using Quantity)");
        }

        if (missing.Count > 0)
        {
            return (
                false,
                $"Missing required column(s): {string.Join(", ", missing)}."
            );
        }

        return (true, string.Empty);
    }

    public static bool TryResolveCustomer(
        string customerCode,
        string customerName,
        string? province,
        string? cityMunicipality,
        string? customerType,
        string? address,
        IReadOnlyDictionary<string, Data.Customer> customerByCode,
        IReadOnlyDictionary<string, Data.Customer> customerByName,
        IEnumerable<Data.Customer> allCustomers,
        out Data.Customer? customer,
        out List<Data.Customer>? suggestions)
    {
        suggestions = null;
        var allCustomersList = allCustomers.ToList();

        // STEP 1: Match by CustomerCode
        if (!string.IsNullOrWhiteSpace(customerCode))
        {
            if (customerByCode.TryGetValue(NormalizeCustomerLookup(customerCode), out var found))
            {
                customer = found;
                return true;
            }
        }

        // STEP 2: Find all matching customer names
        var candidates = new List<Data.Customer>();
        var normalizedSearchName = string.Empty;
        if (!string.IsNullOrWhiteSpace(customerName))
        {
            normalizedSearchName = NormalizeCustomerLookup(customerName);
            candidates = allCustomersList
                .Where(c => NormalizeCustomerLookup(c.CustomerName ?? string.Empty) == normalizedSearchName)
                .ToList();

            if (candidates.Count == 1)
            {
                customer = candidates[0];
                return true;
            }
        }

        if (candidates.Count == 0)
        {
            customer = null!;
            if (!string.IsNullOrWhiteSpace(normalizedSearchName))
            {
                suggestions = allCustomersList
                    .Where(c => NormalizeCustomerLookup(c.CustomerName ?? string.Empty).Contains(normalizedSearchName))
                    .Take(5)
                    .ToList();
            }
            else
            {
                suggestions = null;
            }

            return false;
        }

        // STEP 3: Filter by Province
        if (!string.IsNullOrWhiteSpace(province))
        {
            var normalizedProvince = Normalize(province);
            var filtered = candidates
                .Where(c => Normalize(c.Province ?? string.Empty) == normalizedProvince)
                .ToList();

            if (filtered.Count == 1)
            {
                customer = filtered[0];
                return true;
            }

            if (filtered.Count > 0)
                candidates = filtered;
        }

        // STEP 4: Filter by CityMunicipality
        if (!string.IsNullOrWhiteSpace(cityMunicipality))
        {
            var normalizedCity = Normalize(cityMunicipality);
            var filtered = candidates
                .Where(c => Normalize(c.City ?? string.Empty) == normalizedCity)
                .ToList();

            if (filtered.Count == 1)
            {
                customer = filtered[0];
                return true;
            }

            if (filtered.Count > 0)
                candidates = filtered;
        }

        // STEP 5: Filter by CustomerType
        if (!string.IsNullOrWhiteSpace(customerType))
        {
            var normalizedType = Normalize(customerType);
            var filtered = candidates
                .Where(c => Normalize(c.CustomerType ?? string.Empty) == normalizedType)
                .ToList();

            if (filtered.Count == 1)
            {
                customer = filtered[0];
                return true;
            }

            if (filtered.Count > 0)
                candidates = filtered;
        }

        // STEP 6: Filter by Address
        if (!string.IsNullOrWhiteSpace(address))
        {
            var normalizedAddress = NormalizeAddress(address);

            var filtered = candidates
                .Where(c => NormalizeAddress(c.AddressLine ?? string.Empty).Contains(normalizedAddress))
                .ToList();

            if (filtered.Count == 1)
            {
                customer = filtered[0];
                return true;
            }

            if (filtered.Count > 0)
                candidates = filtered;
        }

        customer = null!;
        suggestions = candidates.Count > 0 ? candidates : null;
        return false;
    }

    public static bool ResolveOrderType(
    string? orderType,
    decimal? netAmount,
    int? quantity,
    out string normalizedOrderType)
    {
        // Priority 1: If OrderType field is present, it must be valid and is authoritative.
        if (!string.IsNullOrWhiteSpace(orderType))
        {
            if (TryParseOrderType(orderType, out normalizedOrderType))
                return true;

            // OrderType was provided but invalid -> treat as unresolved (caller should report invalid input).
            normalizedOrderType = string.Empty;
            return false;
        }

        // Priority 2: If NetAmount is provided, infer from its sign. Zero counts as Invoice.
        if (netAmount.HasValue)
        {
            normalizedOrderType = netAmount.Value < 0 ? "Credit" : "Invoice";
            return true;
        }

        // Priority 3: Use Quantity sign when present.
        if (quantity.HasValue && quantity.Value != 0)
        {
            normalizedOrderType = quantity.Value < 0 ? "Credit" : "Invoice";
            return true;
        }

        normalizedOrderType = string.Empty;
        return false;
    }
    public static bool TryParseOrderType(string orderType, out string normalizedOrderType)
    {
        // Invoice aliases: Invoice, Order, Sales
        if (string.Equals(orderType, "Invoice", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "Order", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "CS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "INV", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "2I", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "2R", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "ML2I", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "VS2", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "VS1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "Sales", StringComparison.OrdinalIgnoreCase))
        {
            normalizedOrderType = "Invoice";
            return true;
        }

        // Credit aliases: Credit, Returns, CN
        if (string.Equals(orderType, "Credit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "Returns", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "BRG", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderType, "CN", StringComparison.OrdinalIgnoreCase))
        {
            normalizedOrderType = "Credit";
            return true;
        }

        normalizedOrderType = string.Empty;
        return false;
    }

    public static bool TryResolveItem(
    string? skuCode,
    string? itemName,
    IReadOnlyDictionary<string, Data.SubdItem> itemBySku,
    IEnumerable<Data.SubdItem> allItems,
    out Data.SubdItem? item,
    out List<Data.SubdItem>? suggestions)
    {
        suggestions = null;

        // STEP 1: SKU lookup (authoritative)
        if (!string.IsNullOrWhiteSpace(skuCode) &&
            itemBySku.TryGetValue(skuCode.Trim(), out var found))
        {
            item = found;
            return true;
        }

        // STEP 2: Item name fallback
        if (string.IsNullOrWhiteSpace(itemName))
        {
            item = null;
            return false;
        }

        var normalized = NormalizeItemName(itemName);

        var matches = allItems
            .Where(i => NormalizeItemName(i.ItemName) == normalized)
            .ToList();

        if (matches.Count == 1)
        {
            item = matches[0];
            return true;
        }

        if (matches.Count > 1)
        {
            item = null;
            suggestions = matches;
            return false;
        }

        item = null;
        return false;
    }

    public static bool IsMissingUomValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();
        return trimmed == "-" || trimmed == "–" || trimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ResolveUom(
        string? unitOfMeasure,
        out string resolvedUom,
        int? caseQuantity = null,
        int? pieceQuantity = null,
        int? inBoxQuantity = null,
        int? dozenQuantity = null)
    {
        if (!IsMissingUomValue(unitOfMeasure))
        {
            if (TryExtractBracketedUom(unitOfMeasure, out var bracketedUom))
            {
                resolvedUom = NormalizeUom(bracketedUom);
                return true;
            }

            resolvedUom = NormalizeUom(unitOfMeasure);
            return true;
        }

        if (caseQuantity.HasValue && caseQuantity.Value != 0)
        {
            resolvedUom = "CS";
            return true;
        }

        if (pieceQuantity.HasValue && pieceQuantity.Value != 0)
        {
            resolvedUom = "PCS";
            return true;
        }

        if (inBoxQuantity.HasValue && inBoxQuantity.Value != 0)
        {
            resolvedUom = "IB";
            return true;
        }

        if (dozenQuantity.HasValue && dozenQuantity.Value != 0)
        {
            resolvedUom = "DZ";
            return true;
        }

        resolvedUom = string.Empty;
        return false;
    }

    public static bool TryResolveUom(
        int subdItemId,
        string? unitOfMeasure,
        IReadOnlyDictionary<(int subdItemId, string UomName), Data.ItemsUom> uomLookup,
        out Data.ItemsUom? uom)
    {
        uom = null;

        if (string.IsNullOrWhiteSpace(unitOfMeasure))
            return false;

        if (TryExtractBracketedUom(unitOfMeasure, out var bracketedUom))
            unitOfMeasure = bracketedUom;

        var normalizedUom = Normalize(unitOfMeasure);
        return uomLookup.TryGetValue((subdItemId, normalizedUom), out uom);
    }

    private static bool TryExtractBracketedUom(string? value, out string extractedUom)
    {
        extractedUom = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var lastOpen = value.LastIndexOf('[');
        var lastClose = value.LastIndexOf(']');

        if (lastOpen >= 0 && lastClose > lastOpen)
        {
            extractedUom = value.Substring(lastOpen + 1, lastClose - lastOpen - 1).Trim();
            return !string.IsNullOrWhiteSpace(extractedUom);
        }

        return false;
    }
    public static bool TryResolveQuantity(
        int? quantity,
        string? unitOfMeasure,
        int? caseQuantity,
        int? pieceQuantity,
        int? inBoxQuantity,
        int? dozenQuantity,
        out int resolvedQuantity,
        out string resolvedUom,
        out ImportField resolvedField)
    {
        resolvedQuantity = 0;
        resolvedUom = string.Empty;
        resolvedField = default;

        if (quantity.HasValue && quantity.Value != 0)
        {
            resolvedQuantity = quantity.Value;
            resolvedUom = NormalizeUom(unitOfMeasure) ?? "PCS";
            resolvedField = ImportField.Quantity;
            return true;
        }

        // Use the implementation that can derive UOM from split-quantities when unitOfMeasure is missing
        if (!ResolveUom(unitOfMeasure, out var uom, caseQuantity, pieceQuantity, inBoxQuantity, dozenQuantity))
        {
            return false;
        }

        resolvedUom = uom;

        if (string.Equals(uom, "CS", StringComparison.OrdinalIgnoreCase) &&
            caseQuantity.HasValue && caseQuantity.Value != 0)
        {
            resolvedQuantity = caseQuantity.Value;
            resolvedField = ImportField.CaseQuantity;
            return true;
        }

        if (string.Equals(uom, "PCS", StringComparison.OrdinalIgnoreCase) &&
            pieceQuantity.HasValue && pieceQuantity.Value != 0)
        {
            resolvedQuantity = pieceQuantity.Value;
            resolvedField = ImportField.PieceQuantity;
            return true;
        }

        if (string.Equals(uom, "IB", StringComparison.OrdinalIgnoreCase) &&
            inBoxQuantity.HasValue && inBoxQuantity.Value != 0)
        {
            resolvedQuantity = inBoxQuantity.Value;
            resolvedField = ImportField.InBoxQuantity;
            return true;
        }

        if (string.Equals(uom, "DZ", StringComparison.OrdinalIgnoreCase) &&
            dozenQuantity.HasValue && dozenQuantity.Value != 0)
        {
            resolvedQuantity = dozenQuantity.Value;
            resolvedField = ImportField.DozenQuantity;
            return true;
        }

        return false;
    }
    public enum ImportField
    {
        Quantity,
        CaseQuantity,
        PieceQuantity,
        InBoxQuantity,
        DozenQuantity
    }

    public static string ValidateGroupConsistency(List<ImportedInvoiceRow> rows)
    {
        // All rows in same invoice must have same InvoiceDate
        if (rows.Select(row => row.InvoiceDate).Distinct().Count() > 1)
            return "Invoice date values must be the same for all rows in the same invoice.";

        // All rows must have same CustomerCode
        if (rows.Select(row => Normalize(row.CustomerCode)).Distinct().Count() > 1)
            return "Customer code values must be the same for all rows in the same invoice.";

        return string.Empty;
    }
    
    public static IEnumerable<string> GetUomSynonyms(string value)
    {
        var normalized = Normalize(value);

        switch (normalized)
        {
            case "cs":
            case "case":
                yield return "case";
                yield return "cs";
                yield break;
            case "dz":
            case "dozen":
                yield return "dozen";
                yield return "dz";
                yield break;
            case "pc":
            case "pcs":
            case "piece":
                yield return "piece";
                yield return "pc";
                yield return "pcs";
                yield break;
            case "pckg":
            case "pck":
            case "pack":
            case "package":
                yield return "package";
                yield return "pckg";
                yield break;
            default:
                yield return value.Trim();
                yield break;
        }
    }

    // Helpers (can move to ImportSalesInvoiceHelpers.cs later)
    private static string Normalize(string value)
        => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeCustomerLookup(string value)
        => Normalize(value)
            .Replace("'", string.Empty)
            .Replace("`", string.Empty)
            .Replace("'", string.Empty);

    private static string NormalizeAddress(string value)
    {
        return new string(
            value
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
    }
    public static string NormalizeUomName(string value)
    {
        var normalized = Normalize(value);

        return normalized switch
        {
            "cs" or "case" => "Case",
            "dz" or "dozen" => "Dozen",
            "pc" or "pcs" or "piece" => "Piece",
            "pckg" or "package" => "Package",
            "pck" or "pack" => value.Trim(),
            _ => value.Trim()
        };
    }
    private static string NormalizeUom(string? value)
        => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string NormalizeItemName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = value
            .Trim()
            .ToUpperInvariant()
            .Replace(".", "")
            .Replace(" ", "");

        return cleaned;
    }

}




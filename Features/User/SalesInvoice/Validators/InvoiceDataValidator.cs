using STTproject.Features.Admin.ImportTemplate.Services;
using STTproject.Features.User.SalesInvoice.DTOs;
using System.Text.RegularExpressions;
using System.Globalization;


namespace STTproject.Features.User.SalesInvoice.Services;

public sealed class InvoiceDataValidator
{
    /// <summary>
    /// The registry is the one definition of what a Sales Invoice template must map, so this only asks it.
    /// </summary>
    public static (bool IsValid, string ErrorMessage) ValidateRequiredHeaders(
        IReadOnlyDictionary<string, int> headers)
    {
        var missing = ImportFieldRegistry.MissingRequired("SalesInvoice", headers.Keys);
        return missing.Count == 0
            ? (true, string.Empty)
            : (false, WorkbookReader.MissingColumnsMessage(missing));
    }
    public static bool TryResolveCustomer(string customerCode, string customerName, string? province, string? cityMunicipality,
        string? customerType, string? address, IReadOnlyDictionary<string, Data.Customer> customerByCode, IEnumerable<Data.Customer> allCustomers,
        out Data.Customer? customer, out List<Data.Customer>? suggestions)
        => TryResolveCustomer(customerCode, customerName, province, cityMunicipality, customerType, address, customerByCode, allCustomers,
        out customer, out suggestions, out _);

    /// <summary>
    /// Resolves the file's customer to one Customer row. A customer is one row per (Subd, Customer Code); how the subdistributor
    /// identifies it (its own code and/or store name) lives in Customer.SubdCustomers, so <paramref name="allCustomers"/> must be
    /// loaded with SubdCustomers included.
    /// </summary>
    public static bool TryResolveCustomer(string customerCode, string customerName, string? province, string? cityMunicipality, string? customerType,
        string? address, IReadOnlyDictionary<string, Data.Customer> customerByCode, IEnumerable<Data.Customer> allCustomers,
        out Data.Customer? customer, out List<Data.Customer>? suggestions, out List<Data.Customer>? ambiguousCandidates)
    {
        suggestions = null;
        ambiguousCandidates = null;
        var allCustomersList = allCustomers.ToList();
        var candidates = new List<Data.Customer>();

        // STEP 1: Match by CustomerCode
        if (!string.IsNullOrWhiteSpace(customerCode))
        {
            var normalizedCode = NormalizeCustomerLookup(customerCode);

            if (customerByCode.TryGetValue(normalizedCode, out var found))
            {
                customer = found;
                return true;
            }

            // STEP 1.5: Not a CustomerCode — the import may instead reference the subdistributor's own code for this customer.
            var subdCodeMatches = allCustomersList
                .Where(c => HasSubdCode(c, normalizedCode))
                .ToList();

            if (TryCollapseToSingleLogicalCustomer(subdCodeMatches, out var collapsedFromCode))
            {
                customer = collapsedFromCode;
                return true;
            }

            if (subdCodeMatches.Count > 1)
            {
                if (!string.IsNullOrWhiteSpace(customerName))
                {
                    var searchName = NormalizeCustomerLookup(StripCodePrefix(customerName));

                    // Same code under several customers: the one whose name (company or subd name for THAT code) matches.
                    var narrowed = subdCodeMatches
                        .Where(c => NormalizeCustomerLookup(c.CustomerName ?? string.Empty) == searchName ||
                                    c.SubdCustomers.Any(m =>
                                        NormalizeCustomerLookup(m.SubdCustCode) == normalizedCode &&
                                        NormalizeCustomerLookup(m.SubdCustName) == searchName))
                        .ToList();

                    if (narrowed.Count == 0)
                    {
                        narrowed = subdCodeMatches.Where(c => c.SubdCustomers.Any(m =>
                        {
                            if (NormalizeCustomerLookup(m.SubdCustCode) != normalizedCode) return false;
                            var sub = NormalizeCustomerLookup(m.SubdCustName);
                            return sub.Length >= 8 && searchName.EndsWith(sub);
                        })).ToList();
                    }

                    if (TryCollapseToSingleLogicalCustomer(narrowed, out var collapsedNarrowed))
                    {
                        customer = collapsedNarrowed;
                        return true;
                    }

                    if (narrowed.Count > 0)
                        candidates = narrowed;
                }
                else
                {
                    candidates = subdCodeMatches;
                }
            }
        }

        // STEP 1.9: The file has no customer code, only a name. If exactly one customer carries that name as a subd store
        // name (an admin mapping, or one remembered from a confirmed import), use it before fuzzy name matching.
        if (candidates.Count == 0 && string.IsNullOrWhiteSpace(customerCode) && !string.IsNullOrWhiteSpace(customerName))
        {
            var searchSubdName = NormalizeCustomerLookup(customerName);
            var subdNameMatches = allCustomersList
                .Where(c => HasSubdName(c, NormalizeCustomerLookup, searchSubdName))
                .ToList();

            if (TryCollapseToSingleLogicalCustomer(subdNameMatches, out var collapsedSubdName))
            {
                customer = collapsedSubdName;
                return true;
            }
        }

        // STEP 2: Try raw name match first (strip code prefix only, keep everything else)
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(customerName))
        {
            var rawName = StripCodePrefix(customerName); // extracts "10798 - TONDO" → "TONDO"

            candidates = allCustomersList
                .Where(c => StripCodePrefix(c.CustomerName ?? string.Empty)
                                .Equals(rawName, StringComparison.OrdinalIgnoreCase) ||
                            c.SubdCustomers.Any(m => m.SubdCustName.Length > 0 &&
                                StripCodePrefix(m.SubdCustName).Equals(rawName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (TryCollapseToSingleLogicalCustomer(candidates, out var collapsedRaw))
            {
                customer = collapsedRaw;
                return true;
            }

            // STEP 2.1: Raw match had multiple — try exact normalized (keeps parens/slash/# intact)
            if (candidates.Count > 1)
            {
                var normalizedSearchName = NormalizeCustomerLookup(customerName);
                var exactFiltered = candidates
                    .Where(c => NormalizeCustomerLookup(c.CustomerName ?? string.Empty) == normalizedSearchName ||
                                HasSubdName(c, NormalizeCustomerLookup, normalizedSearchName))
                    .ToList();

                if (TryCollapseToSingleLogicalCustomer(exactFiltered, out var collapsedExact))
                {
                    customer = collapsedExact;
                    return true;
                }

                if (exactFiltered.Count > 1)
                    candidates = exactFiltered;
            }

            // STEP 2.2: No raw match — try normalized name (keeps parens so TONDO != PANSOL)
            if (candidates.Count == 0)
            {
                var normalizedSearchName = NormalizeCustomerLookup(customerName);
                candidates = allCustomersList
                    .Where(c => NormalizeCustomerLookup(c.CustomerName ?? string.Empty) == normalizedSearchName ||
                                HasSubdName(c, NormalizeCustomerLookup, normalizedSearchName))
                    .ToList();

                if (TryCollapseToSingleLogicalCustomer(candidates, out var collapsedNormalized))
                {
                    customer = collapsedNormalized;
                    return true;
                }
            }
        }

        // STEP 2.5: Still no single match — strip prefix/parens, use location hint as tiebreaker
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(customerName))
        {
            var (strippedName, locationHint) = ExtractCustomerNameParts(customerName);

            candidates = allCustomersList
                .Where(c =>
                {
                    var (dbStripped, _) = ExtractCustomerNameParts(c.CustomerName ?? string.Empty);
                    return dbStripped == strippedName;
                })
                .ToList();

            if (TryCollapseToSingleLogicalCustomer(candidates, out var collapsedStripped))
            {
                customer = collapsedStripped;
                return true;
            }

            // Use parenthetical hint e.g. (TONDO) to break ties
            if (candidates.Count > 1 && !string.IsNullOrWhiteSpace(locationHint))
            {
                var hintFiltered = candidates
                    .Where(c =>
                        Normalize(c.City ?? string.Empty).Contains(locationHint) ||
                        Normalize(c.Province ?? string.Empty).Contains(locationHint) ||
                        Normalize(c.AddressLine ?? string.Empty).Contains(locationHint))
                    .ToList();

                if (TryCollapseToSingleLogicalCustomer(hintFiltered, out var collapsedHint))
                {
                    customer = collapsedHint;
                    return true;
                }

                if (hintFiltered.Count > 1)
                    candidates = hintFiltered;
            }

            // STEP 2.6: Compact fallback — strips all spaces
            // handles CHICO'S STORE → "chicosstore" matching CHICO S STORE → "chicosstore"
            if (candidates.Count == 0)
            {
                var compactSearch = NormalizeCustomerLookupCompact(customerName);
                candidates = allCustomersList
                    .Where(c => NormalizeCustomerLookupCompact(c.CustomerName ?? string.Empty) == compactSearch ||
                                HasSubdName(c, NormalizeCustomerLookupCompact, compactSearch))
                    .ToList();

                if (TryCollapseToSingleLogicalCustomer(candidates, out var collapsedCompact))
                {
                    customer = collapsedCompact;
                    return true;
                }

                // Truly no match — exit with suggestions
                if (candidates.Count == 0)
                {
                    var (strippedForSuggestion, _) = ExtractCustomerNameParts(customerName);
                    customer = null!;
                    suggestions = !string.IsNullOrWhiteSpace(strippedForSuggestion)
                        ? allCustomersList
                            .Where(c =>
                            {
                                var (dbStripped, _) = ExtractCustomerNameParts(c.CustomerName ?? string.Empty);
                                return dbStripped.Contains(strippedForSuggestion);
                            })
                            .Take(5)
                            .ToList()
                        : null;
                    return false;
                }
                // Count > 1 → fall through to Steps 3-6
            }
        }

        // STEP 3: Filter by Province
        if (!string.IsNullOrWhiteSpace(province))
        {
            var normalizedProvince = Normalize(province);
            var filtered = candidates
                .Where(c => Normalize(c.Province ?? string.Empty) == normalizedProvince)
                .ToList();

            if (TryCollapseToSingleLogicalCustomer(filtered, out var collapsedProvince))
            {
                customer = collapsedProvince;
                return true;
            }
            if (filtered.Count > 0) candidates = filtered;
        }

        // STEP 4: Filter by CityMunicipality
        if (!string.IsNullOrWhiteSpace(cityMunicipality))
        {
            var normalizedCity = Normalize(cityMunicipality);
            var filtered = candidates
                .Where(c =>
                {
                    var dbCity = Normalize(c.City ?? string.Empty);
                    return dbCity.Contains(normalizedCity) || normalizedCity.Contains(dbCity);
                })
                .ToList();

            if (TryCollapseToSingleLogicalCustomer(filtered, out var collapsedCity))
            {
                customer = collapsedCity;
                return true;
            }
            if (filtered.Count > 0) candidates = filtered;
        }

        // STEP 5: Filter by CustomerType
        if (!string.IsNullOrWhiteSpace(customerType))
        {
            var normalizedType = Normalize(customerType);
            var filtered = candidates
                .Where(c => Normalize(c.CustomerType ?? string.Empty) == normalizedType)
                .ToList();

            if (TryCollapseToSingleLogicalCustomer(filtered, out var collapsedType))
            {
                customer = collapsedType;
                return true;
            }
            if (filtered.Count > 0) candidates = filtered;
        }

        // STEP 6: Filter by Address
        if (!string.IsNullOrWhiteSpace(address))
        {
            var normalizedAddress = NormalizeAddress(address);
            var filtered = candidates
                .Where(c => NormalizeAddress(c.AddressLine ?? string.Empty).Contains(normalizedAddress))
                .ToList();

            if (TryCollapseToSingleLogicalCustomer(filtered, out var collapsedAddress))
            {
                customer = collapsedAddress;
                return true;
            }
            if (filtered.Count > 0) candidates = filtered;
        }

        customer = null!;
        suggestions = candidates.Count > 0 ? candidates : null;
        var distinct = candidates
            .GroupBy(c => c.CustomerId)
            .Select(g => g.First())
            .OrderBy(c => c.CustomerId)
            .ToList();
        ambiguousCandidates = distinct.Count > 1 ? distinct : null;
        return false;
    }

    // Does any of the customer's subd mappings carry this (normalized) subd customer code?
    private static bool HasSubdCode(Data.Customer c, string normalizedCode) =>
        normalizedCode.Length > 0 &&
        c.SubdCustomers.Any(m => m.SubdCustCode.Length > 0 && NormalizeCustomerLookup(m.SubdCustCode) == normalizedCode);

    // Does any of the customer's subd mappings carry this subd store name, compared with the given normalizer?
    private static bool HasSubdName(Data.Customer c, Func<string, string> normalize, string normalizedName) =>
        normalizedName.Length > 0 &&
        c.SubdCustomers.Any(m => m.SubdCustName.Length > 0 && normalize(m.SubdCustName) == normalizedName);

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

    public static bool IsFreeItemValue(string? value)
    {
        value = value?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value == "free" || value == "tdi-claims" || value == "0")
            return true;

        if (decimal.TryParse(
                value,
                System.Globalization.NumberStyles.Number | System.Globalization.NumberStyles.AllowCurrencySymbol | System.Globalization.NumberStyles.AllowParentheses,
                System.Globalization.CultureInfo.InvariantCulture,
                out var numericValue))
        {
            return numericValue == 0;
        }

        return false;
    }

    public static bool TryParseOrderType(string orderType, out string normalizedOrderType)
    {
        var normalized = orderType?.Trim().ToLowerInvariant() ?? string.Empty;
        // Invoice aliases: Invoice, Order, Sales
        if (string.Equals(normalized, "Invoice", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Order", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "CS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "INV", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "2I", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "2R", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ML2I", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "VS2", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "VS1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Sales", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Sales Receipt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "TDI-Claims", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "TDI Claims", StringComparison.OrdinalIgnoreCase))
        {
            normalizedOrderType = "Invoice";
            return true;
        }

        // Credit aliases: Credit, Returns, CN
        if (string.Equals(normalized, "Credit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Returns", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Return", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "BRG", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "CN", StringComparison.OrdinalIgnoreCase))
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
        ILookup<string, Data.SubdItem> itemsBySku,
        IEnumerable<Data.SubdItem> allItems,
        out Data.SubdItem? item,
        out List<Data.SubdItem>? suggestions,
        out string? warning,
        out List<Data.SubdItem>? ambiguousCandidates)
    {
        suggestions = null;
        warning = null;
        ambiguousCandidates = null;

        if (!string.IsNullOrWhiteSpace(skuCode))
        {
            var skuMatches = itemsBySku[Normalize(skuCode.Trim())].ToList();

            if (skuMatches.Count == 1)
            {
                item = skuMatches[0];
                return true;
            }

            if (skuMatches.Count > 1)
            {
                var effectiveCandidates = skuMatches;
                var narrowedByName = false;

                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    var normalizedName = NormalizeItemName(itemName);
                    var narrowed = skuMatches.Where(i => NormalizeItemName(i.ItemName) == normalizedName).ToList();
                    if (narrowed.Count >= 1)
                    {
                        effectiveCandidates = narrowed;
                        narrowedByName = true;
                    }
                }

                item = effectiveCandidates.OrderBy(i => i.SubdItemId).First();

                if (effectiveCandidates.Count > 1)
                {
                    ambiguousCandidates = effectiveCandidates;

                    warning = narrowedByName
                        ? $"SKU '{skuCode}' matched {effectiveCandidates.Count} records with the same code and name. Pick the right one."
                        : $"SKU '{skuCode}' matched {effectiveCandidates.Count} different products under the same code. Pick the right one.";
                }

                return true;
            }
        }

        // STEP 1.5: The generated template's item picker (column E) stores the item as a
        // combined "Code - Name" string with no separate SkuCode column, so when skuCode
        // is empty, try pulling the leading code segment out of itemName and matching by SKU.
        if (string.IsNullOrWhiteSpace(skuCode) && !string.IsNullOrWhiteSpace(itemName))
        {
            var extractedCode = ExtractLeadingCode(itemName);
            if (!string.IsNullOrWhiteSpace(extractedCode))
            {
                var matchesByExtractedCode = itemsBySku[Normalize(extractedCode!.Trim())].ToList();

                if (matchesByExtractedCode.Count == 1)
                {
                    item = matchesByExtractedCode[0];
                    return true;
                }

                if (matchesByExtractedCode.Count > 1)
                {
                    var normalizedName = NormalizeItemName(itemName);
                    var narrowed = matchesByExtractedCode
                        .Where(i => NormalizeItemName(i.ItemName) == normalizedName)
                        .ToList();

                    if (narrowed.Count == 1)
                    {
                        item = narrowed[0];
                        return true;
                    }

                    if (narrowed.Count > 1)
                    {
                        item = narrowed.OrderBy(i => i.SubdItemId).First();
                        ambiguousCandidates = narrowed;
                        warning = $"Item '{itemName.Trim()}' matched {narrowed.Count} records with the same code and name. Pick the right one.";
                        return true;
                    }

                    item = matchesByExtractedCode.OrderBy(i => i.SubdItemId).First();
                    ambiguousCandidates = matchesByExtractedCode;
                    var candidateNames = string.Join(" / ", matchesByExtractedCode.Select(i => i.ItemName));
                    warning = $"Extracted code '{extractedCode}' matched multiple items. Pick the right one.";
                    return true;
                }
            }
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
        var nameUsed = itemName;

        // STEP 2.5: strip a leading item no / "Code - " prefix and retry against the name.
        if (matches.Count == 0)
        {
            var strippedName = StripItemCodePrefix(itemName, skuCode);
            if (!string.Equals(strippedName, itemName.Trim(), StringComparison.Ordinal))
            {
                var normalizedStripped = NormalizeItemName(strippedName);
                matches = allItems
                    .Where(i => NormalizeItemName(i.ItemName) == normalizedStripped)
                    .ToList();
                nameUsed = strippedName;
            }
        }

        if (matches.Count > 0)
            return ResolveNameMatches(matches, nameUsed, out item, out warning, out ambiguousCandidates);

        item = null;
        return false;
    }

    public static bool TryResolveFallbackUom(
        out Data.ItemsUom? uom,
        out string? warning,
        out List<Data.ItemsUom>? reviewCandidates)
    {
        uom = null;
        warning = null;
        reviewCandidates = null;
        return false;
    }

    public static bool IsMissingUomValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();
        return trimmed == "-" || trimmed == "–" || trimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The file's own UOM text, bracketed part preferred ("Pack [CS]" → "CS"). No unit is ever guessed:
    /// split quantity columns imply their unit themselves, and which spellings mean the same unit is up to
    /// the admin's UOM synonym groups. The split-quantity parameters are kept only so existing callers compile.
    /// </summary>
    public static bool ResolveUom(
        string? unitOfMeasure,
        out string resolvedUom,
        int? caseQuantity = null,
        int? pieceQuantity = null,
        int? inBoxQuantity = null,
        int? dozenQuantity = null)
    {
        if (IsMissingUomValue(unitOfMeasure))
        {
            resolvedUom = string.Empty;
            return false;
        }

        resolvedUom = TryExtractBracketedUom(unitOfMeasure, out var bracketedUom)
            ? NormalizeUom(bracketedUom)
            : NormalizeUom(unitOfMeasure);
        return true;
    }

    public static bool TryResolveUom(
    int subdItemId,
    string? unitOfMeasure,
    ILookup<(int subdItemId, string UomName), Data.ItemsUom> uomLookup,
    out Data.ItemsUom? uom,
    out List<Data.ItemsUom>? ambiguousMatches)
    {
        uom = null;
        ambiguousMatches = null;

        if (string.IsNullOrWhiteSpace(unitOfMeasure))
            return false;

        if (TryExtractBracketedUom(unitOfMeasure, out var bracketedUom))
            unitOfMeasure = bracketedUom;

        unitOfMeasure = UomParenRegex.Replace(unitOfMeasure, string.Empty).Trim();

        var normalizedUom = Normalize(unitOfMeasure);
        var matches = uomLookup[(subdItemId, normalizedUom)].Where(u => u.IsActive).ToList();

        if (matches.Count == 0)
            return false; // genuinely missing — stays an ERROR

        if (matches.Count > 1)
        {
            // Same UOM name configured more than once for this item — not missing,
            // just ambiguous. Surface as a WARNING and let the user pick.
            ambiguousMatches = matches;
            uom = matches.OrderBy(u => u.ItemsUomId).First();
            return true;
        }

        uom = matches[0];
        return true;
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

    /// <summary>
    /// A single Quantity column with its Unit of Measure. Split quantity columns (Case/Dozen/Piece/In-Box) are read
    /// row by row in the import itself, so their parameters here are unused and kept only so existing callers compile.
    /// </summary>
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

        if (!quantity.HasValue || quantity.Value == 0)
            return false;

        if (!ResolveUom(unitOfMeasure, out var uom))
            return false;

        resolvedQuantity = quantity.Value;
        resolvedUom = uom;
        resolvedField = ImportField.Quantity;
        return true;
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

        // All rows must resolve to the same customer
        var distinctCustomerKeys = rows
            .Select(row => row.ResolvedCustomerId > 0
                ? row.ResolvedCustomerId.ToString(CultureInfo.InvariantCulture)
                : Normalize(row.ResolvedCustomerCode ?? row.CustomerCode ?? row.CustomerName ?? string.Empty))
            .Distinct()
            .Count();

        if (distinctCustomerKeys > 1)
            return "Customer values must be the same for all rows in the same invoice.";

        var salesmen = rows
            .Select(r => r.SalesManName?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (salesmen > 1)
            return "Salesman values must be the same for all rows in the same invoice.";

        return string.Empty;
    }

    private static string Normalize(string value)
        => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static (string normalizedName, string? locationHint) ExtractCustomerNameParts(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (string.Empty, null);

        var cleaned = value.Trim()
            .Replace("'", "")
            .Replace("'", "")
            .Replace("`", "");

        // Strip leading code prefix like "10798 - "
        var dashIndex = cleaned.IndexOf(" - ");
        if (dashIndex >= 0)
            cleaned = cleaned[(dashIndex + 3)..];

        // Strip trailing noise suffixes like "##", "/ ##"
        cleaned = Regex.Replace(cleaned, @"[\s/]*#+\s*$", string.Empty).Trim();

        // Extract parenthetical as location hint BEFORE stripping
        string? locationHint = null;
        var parenMatch = Regex.Match(cleaned, @"\((.+?)\)");
        if (parenMatch.Success)
        {
            locationHint = parenMatch.Groups[1].Value.Trim().ToLowerInvariant();
            cleaned = cleaned.Replace(parenMatch.Value, string.Empty);
        }

        cleaned = Regex.Replace(cleaned.ToLowerInvariant(), @"[^a-z0-9\s]", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return (cleaned, locationHint);
    }
    private static string NormalizeCustomerLookup(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var cleaned = value.Trim()
            .Replace("'", "")
            .Replace("'", "")
            .Replace("`", "")
            .Replace("\"", "");

        // Strip trailing noise suffixes like "##", "/ ##", "/ #", "#"
        cleaned = Regex.Replace(cleaned, @"[\s/]*#+\s*$", string.Empty).Trim();

        // Keep parens, slash so other suffixes stay distinct
        cleaned = Regex.Replace(cleaned.ToLowerInvariant(), @"[^a-z0-9\s()/#]", string.Empty);
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
    private static string NormalizeAddress(string value)
    {
        return new string(
            value
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
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

    private static string NormalizeCustomerLookupCompact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var cleaned = value.Trim()
            .Replace("'", "").Replace("'", "").Replace("`", "").Replace("\"", "");

        // Strip everything except letters and digits (no spaces either)
        return Regex.Replace(cleaned.ToLowerInvariant(), @"[^a-z0-9]", string.Empty);
    }
    private static string StripCodePrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var dashIndex = value.IndexOf(" - ");
        return dashIndex >= 0 ? value[(dashIndex + 3)..].Trim() : value.Trim();
    }

    // Each customer is one row now, so a set of candidates is "one customer" only when it holds a single CustomerId.
    private static bool TryCollapseToSingleLogicalCustomer(List<Data.Customer> candidates, out Data.Customer? customer)
    {
        if (candidates.Count == 0)
        {
            customer = null;
            return false;
        }

        var first = candidates[0];
        if (candidates.All(c => c.CustomerId == first.CustomerId))
        {
            customer = first;
            return true;
        }

        customer = null;
        return false;
    }

    private static readonly Regex BarcodePrefixRegex = new(@"^\s*(?<code>\d{5,})\s+(?<rest>.+)$", RegexOptions.Compiled);

    private static string StripItemCodePrefix(string value, string? skuCode = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();

        if (!string.IsNullOrWhiteSpace(skuCode))
        {
            var code = skuCode.Trim();
            if (value.StartsWith(code + " ", StringComparison.OrdinalIgnoreCase))
                return value[code.Length..].Trim();
        }

        var dashIndex = value.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex >= 0) return value[(dashIndex + 3)..].Trim();

        var m = BarcodePrefixRegex.Match(value);
        return m.Success ? m.Groups["rest"].Value.Trim() : value;
    }
    private static string? ExtractLeadingCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var dashIndex = value.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex >= 0) return value[..dashIndex].Trim();

        var m = BarcodePrefixRegex.Match(value);
        return m.Success ? m.Groups["code"].Value : null;
    }

    private static bool ResolveNameMatches(List<Data.SubdItem> matches, string rawName, out Data.SubdItem? item, out string? warning, out List<Data.SubdItem>? ambiguousCandidates)
    {
        warning = null;
        ambiguousCandidates = null;
        item = null;

        if (matches.Count == 0) return false;
        if (matches.Count == 1) { item = matches[0]; return true; }

        var exact = matches
            .Where(i => string.Equals(i.ItemName?.Trim(), rawName.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exact.Count == 1) { item = exact[0]; return true; }

        var candidates = exact.Count > 1 ? exact : matches;
        item = candidates.OrderBy(i => i.SubdItemId).First();
        ambiguousCandidates = candidates;
        warning = $"Item name '{rawName.Trim()}' matched {candidates.Count} records. Pick the right one.";
        return true;
    }
    private static readonly Regex UomParenRegex = new(@"\s*\(.*?\)\s*$", RegexOptions.Compiled);
}
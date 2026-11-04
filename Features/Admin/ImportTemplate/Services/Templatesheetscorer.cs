using STTproject.Features.Admin.ImportTemplate.DTOs;

namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>How well one inspected worksheet fits one template sheet. Pure data: nothing is assigned here.</summary>
/// <param name="Eligible">The worksheet may be assigned to this template sheet at all.</param>
/// <param name="Score">0..1. Zero when not eligible. Comparable across the cells of one matching matrix.</param>
/// <param name="Header">Best header block found (Headers mode only); null otherwise.</param>
/// <param name="Reason">Why it's ineligible, or a short note on what matched. For the tester.</param>
public sealed record SheetMatchScore(
    bool Eligible,
    double Score,
    int MatchedFields,
    int ConfiguredFields,
    int RequiredMatched,
    int RequiredFields,
    HeaderCandidate? Header,
    string Reason)
{
    /// <summary>Distinct matched fields / distinct configured fields. Optional fields count here.</summary>
    public double Coverage => ConfiguredFields == 0 ? 0 : (double)MatchedFields / ConfiguredFields;

    /// <summary>Matched required fields / required fields. 1 when the sheet has no required fields.</summary>
    public double RequiredCoverage => RequiredFields == 0 ? 1 : (double)RequiredMatched / RequiredFields;

    public static SheetMatchScore No(string reason) => new(false, 0, 0, 0, 0, 0, null, reason);
}

public static class TemplateSheetScorer
{
    // Name/position modes carry no header evidence, so they get fixed tiers. "Any" is deliberately the weakest,
    // so it only ever takes a worksheet nothing more specific wants.
    public const double ExactScore = 1.0;
    public const double PositionScore = 1.0;
    public const double ContainsScore = 0.75;
    public const double AnyScore = 0.1;

    // Headers mode: mostly "how much of the template is here", partly "are the required ones here". Tunable.
    public const double CoverageWeight = 0.7;
    public const double RequiredWeight = 0.3;

    internal sealed record ConfiguredField(HashSet<string> Aliases, bool Required);

    // The one field-grouping rule. HeaderRowFinder shares it; TemplateSheetResolver.ConfiguredFields is the old copy and goes with the old scorer.
    // A field's header set is its primary header plus every alias, so a file using any of them counts the field once.
    internal static List<ConfiguredField> Fields(ImportTemplateSheetEditDto ts) =>
        ts.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldKey))
            .GroupBy(c => c.FieldKey!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ConfiguredField(TemplateSheetResolver.NormalizedHeaders(g), g.Any(c => c.IsRequired)))
            .Where(f => f.Aliases.Count > 0)
            .ToList();

    public static SheetMatchScore Score(ImportTemplateSheetEditDto ts, WorksheetInspection sheet)
    {
        if (ts.SheetMatchMode == SheetMatchModes.Ignore) return SheetMatchScore.No("This template sheet is set to Ignore.");
        if (!sheet.IsVisible) return SheetMatchScore.No($"Sheet \"{sheet.Name}\" is hidden, so it isn't matched automatically.");

        var value = ts.SheetMatchValue?.Trim() ?? "";

        switch (ts.SheetMatchMode)
        {
            case SheetMatchModes.Any:
                return sheet.IsEmpty
                    ? SheetMatchScore.No($"Sheet \"{sheet.Name}\" is empty.")
                    : Fixed(AnyScore, "Any sheet.");

            case SheetMatchModes.Exact:
                return string.Equals(sheet.Name, value, StringComparison.OrdinalIgnoreCase)
                    ? Fixed(ExactScore, "Name matches exactly.")
                    : SheetMatchScore.No($"Name isn't \"{value}\".");

            case SheetMatchModes.Contains:
                return value.Length > 0 && sheet.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
                    ? Fixed(ContainsScore, $"Name contains \"{value}\".")
                    : SheetMatchScore.No($"Name doesn't contain \"{value}\".");

            case SheetMatchModes.Position:
                return int.TryParse(value, out var p) && sheet.VisibleOrdinal == p
                    ? Fixed(PositionScore, $"Visible sheet number {p}.")
                    : SheetMatchScore.No($"Not visible sheet number {value}.");

            case SheetMatchModes.Headers:
                return ScoreHeaders(ts, sheet);

            default:
                return SheetMatchScore.No("Unknown sheet matching mode.");
        }
    }

    private static SheetMatchScore Fixed(double score, string reason) =>
        new(true, score, 0, 0, 0, 0, null, reason);

    private static SheetMatchScore ScoreHeaders(ImportTemplateSheetEditDto ts, WorksheetInspection sheet)
    {
        if (sheet.IsEmpty) return SheetMatchScore.No($"Sheet \"{sheet.Name}\" is empty.");

        var fields = Fields(ts);
        if (fields.Count == 0) return SheetMatchScore.No("This template sheet has no columns to match on.");

        var count = Math.Max(1, ts.HeaderRowCount);
        var candidates = ts.HeaderRowMode == HeaderRowModes.Fixed
            ? new[] { sheet.GetCandidate(ts.HeaderRowNumber ?? 1, count) }
            : sheet.ScanCandidates(count, Math.Min(sheet.LastRow, TemplateSheetResolver.HeaderScanRows)).ToArray();

        // Best block = most fields, then most required fields, then earliest row. Same order the importer uses.
        (HeaderCandidate Cand, int Matched, int ReqMatched)? best = null;
        foreach (var cand in candidates)
        {
            var present = cand.Keys.ToHashSet();
            var matched = fields.Count(f => f.Aliases.Overlaps(present));
            if (matched == 0) continue;
            var reqMatched = fields.Count(f => f.Required && f.Aliases.Overlaps(present));

            if (best is null || matched > best.Value.Matched
                || (matched == best.Value.Matched && reqMatched > best.Value.ReqMatched))
                best = (cand, matched, reqMatched);
        }

        var configured = fields.Count;
        var required = fields.Count(f => f.Required);

        if (best is null)
            return new(false, 0, 0, configured, 0, required, null,
                $"None of the expected headers appear in the first {TemplateSheetResolver.HeaderScanRows} rows.");

        var (header, m, rm) = best.Value;
        var reqCoverage = required == 0 ? 1 : (double)rm / required;

        // Acceptance gates. Only the minimum field count and the required share decide eligibility;
        // missing optional fields never do, and missing required ones are reported later by column resolution.
        if (m < Math.Min(TemplateSheetResolver.MinHeaderFields, configured))
            return new(false, 0, m, configured, rm, required, header,
                $"Closest header row ({header.Row}) has only {m} of {configured} expected fields.");

        if (required > 0 && reqCoverage < TemplateSheetResolver.MinRequiredShare)
            return new(false, 0, m, configured, rm, required, header,
                $"Closest header row ({header.Row}) has only {rm} of {required} required fields.");

        var score = CoverageWeight * ((double)m / configured) + RequiredWeight * reqCoverage;
        return new(true, score, m, configured, rm, required, header,
            $"Header row {header.Row}: {m} of {configured} fields, {rm} of {required} required.");
    }
}
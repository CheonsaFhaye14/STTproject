namespace STTproject.Features.User.MapItem.DTOs;

public enum StockUomSource
{
	None,
	Explicit,
	Suggested,
	Chosen
}

public enum PriceScope
{
	All,
	StockOnly,
	OthersOnly
}

public enum PriceChoice
{
	Undecided,
	Imported,
	Suggested
}

public sealed class ImportMapItemResult
{
	public List<ImportMapItemRowResult> Rows { get; } = new();
	public List<PreparedItemGroup> PreparedGroups { get; } = new();
	public List<ImportMapItemIssue> Issues { get; } = new();
	public List<string> OriginalHeaders { get; set; } = new();

	public int SuccessCount => Rows.Count(row => row.IsSuccess);

	public int ErrorCount => Rows.Count(row => !row.IsSuccess);

	public bool HasRows => Rows.Count > 0;

	public bool HasIssues => Issues.Count > 0;

	public void AddError(int rowNumber, string subdItemCode, string message)
	{
		Issues.Add(new ImportMapItemIssue(rowNumber, subdItemCode, message));
	}
}

public sealed class ImportMapItemRowResult
{
	public int RowNumber { get; set; }
	public string SubDistributorCode { get; set; } = string.Empty;
	public string SubDistributorName { get; set; } = string.Empty;
	public string Principal { get; set; } = string.Empty;
	public string CompanyItemCode { get; set; } = string.Empty;
	public string CompanyItemName { get; set; } = string.Empty;
	public string SubdItemCode { get; set; } = string.Empty;
	public string SubdItemName { get; set; } = string.Empty;
	public string UomName { get; set; } = string.Empty;
	public int? Conversion { get; set; }
	public decimal? Price { get; set; }

	public string? StockUom { get; set; }

	public bool IsStockUom { get; set; }

	public decimal? ImportedPrice { get; set; }

	public bool PriceFromFile { get; set; }

	public decimal? SuggestedPrice { get; set; }

	public PriceChoice PriceChoice { get; set; }

	public bool HasPricePrompt => SuggestedPrice.HasValue;

	public bool IsManualPrice => HasPricePrompt && PriceChoice == PriceChoice.Imported;

	public bool IsSuccess { get; set; }
	public string Message { get; set; } = string.Empty;
	public int? SubdItemId { get; set; }
	public int? ItemsUomId { get; set; }
	public List<string> Issues { get; } = new();
	public List<string> Warnings { get; } = new();
	public Dictionary<string, string?> RawValues { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PreparedItemGroup
{
	public List<ImportMapItemRowResult> Rows { get; } = new();
	public List<ImportMapItemIssue> Issues { get; } = new();
	public bool Selected { get; set; }
	public bool IsSaved { get; set; }
	public string? SaveErrorMessage { get; set; }

	public string? StockUomName { get; set; }
	public StockUomSource StockUomSource { get; set; }
	public string? StockUomReason { get; set; }

	public bool HasStockUom => !string.IsNullOrWhiteSpace(StockUomName);

	public string? FileStockUom { get; set; }

	public string? SuggestedStockUom { get; set; }
	public string? SuggestedStockUomReason { get; set; }
	public string? CompanyStockUomName { get; set; }
	public decimal? ApplicableStockPrice { get; set; }
	public string? PriceListLabel { get; set; }

	/// <summary>The company item has a Stock UOM, so this mapped item needs one too.</summary>
	public bool RequiresStockUom => !string.IsNullOrWhiteSpace(CompanyStockUomName);

	/// <summary>True when the stock UOM requirement is met, or there is no requirement.</summary>
	public bool StockUomReady => !RequiresStockUom || HasStockUom;

	private const decimal PriceTolerance = 0.01m;

	public int PendingPriceDecisions => Rows.Count(r => r.HasPricePrompt && r.PriceChoice == PriceChoice.Undecided);
	public bool HasPricePrompts => Rows.Any(r => r.HasPricePrompt);
	public bool HasPendingStockPrice => Rows.Any(r => r.IsStockUom && r.HasPricePrompt && r.PriceChoice == PriceChoice.Undecided);
	public bool HasPendingOtherPrices => Rows.Any(r => !r.IsStockUom && r.HasPricePrompt && r.PriceChoice == PriceChoice.Undecided);

	/// <summary>True when a bulk choice for this scope would change something on this item.</summary>
	public bool HasPendingPrices(PriceScope scope) => scope switch
	{
		PriceScope.StockOnly => HasPendingStockPrice,
		// The other UOMs are compared with the stock price, so they wait until that is decided.
		PriceScope.OthersOnly => HasPendingOtherPrices && !HasPendingStockPrice,
		_ => PendingPriceDecisions > 0
	};

	private static bool IsPriceWarning(string w) =>
		w.StartsWith("Stock price", StringComparison.OrdinalIgnoreCase)
		|| w.StartsWith("UOM price", StringComparison.OrdinalIgnoreCase);

	public void RefreshPricePrompts(bool preserveChoices = true)
	{
		var kept = preserveChoices
			? Rows.Where(r => r.HasPricePrompt && r.PriceChoice != PriceChoice.Undecided)
				.ToDictionary(r => r.RowNumber, r => r.PriceChoice)
			: new Dictionary<int, PriceChoice>();

		foreach (var row in Rows)
		{
			row.SuggestedPrice = null;
			row.PriceChoice = PriceChoice.Undecided;
			row.Price = row.ImportedPrice;
			row.Warnings.RemoveAll(IsPriceWarning);
		}

		// Free goods (company item has no Stock UOM) are unpriced by convention (0.00): nothing to compare.
		if (!RequiresStockUom) return;

		void Prompt(ImportMapItemRowResult row, decimal suggested, string warning)
		{
			row.SuggestedPrice = suggested;
			if (kept.TryGetValue(row.RowNumber, out var choice)) row.PriceChoice = choice;
			row.Price = row.PriceChoice == PriceChoice.Suggested ? suggested : row.ImportedPrice;
			if (row.PriceChoice == PriceChoice.Undecided) row.Warnings.Add(warning);
		}

		var stock = Rows.FirstOrDefault(r => r.IsStockUom);
		if (stock is null) return;

		if (stock.PriceFromFile
			&& stock.ImportedPrice is decimal imported
			&& ApplicableStockPrice is decimal company
			&& Math.Abs(imported - company) > PriceTolerance)
		{
			var pct = company == 0 ? (decimal?)null : (imported - company) / company * 100m;
			var pctText = pct.HasValue ? $" ({pct.Value:+0.#;-0.#}%)" : string.Empty;
			var list = string.IsNullOrWhiteSpace(PriceListLabel) ? string.Empty : $" {PriceListLabel}";
			var large = pct.HasValue && Math.Abs(pct.Value) > 50m
				? $" This is a large difference. Check that it is the same unit as the company's {CompanyStockUomName ?? "stock UOM"}."
				: string.Empty;
			Prompt(stock, company,
				$"Stock price {imported:N2} ({stock.UomName}) differs from the company{list} price {company:N2}{pctText}.{large} Choose which to use.");
		}

		var stockPrice = stock.Price;
		var stockConversion = stock.Conversion;
		if (stockPrice is not decimal sp || stockConversion is not > 0) return;

		var stockTakesSuggested = stock.HasPricePrompt && stock.PriceChoice == PriceChoice.Suggested;

		foreach (var row in Rows.Where(r => !r.IsStockUom && r.Conversion is > 0))
		{
			var expected = Math.Round(sp * row.Conversion!.Value / stockConversion.Value, 2, MidpointRounding.AwayFromZero);

			if (!row.PriceFromFile)
			{
				if (stockTakesSuggested) row.Price = expected;
				continue;
			}

			if (row.ImportedPrice is decimal imp && Math.Abs(imp - expected) > PriceTolerance)
			{
				Prompt(row, expected,
					$"UOM price {row.UomName} {imp:N2} differs from the calculated {expected:N2} " +
					$"({stock.UomName} {sp:N2} / {stockConversion} x {row.Conversion}). Choose which to use.");
			}
		}
	}

	public string? DecidePrice(int rowNumber, PriceChoice choice)
	{
		var row = Rows.FirstOrDefault(r => r.RowNumber == rowNumber && r.HasPricePrompt);
		if (row is null) return "This row has no price warning to decide.";

		row.PriceChoice = choice;
		RefreshPricePrompts(preserveChoices: true);
		return null;
	}

	public void DecideAllPrices(PriceChoice choice, PriceScope scope = PriceScope.All)
	{
		if (choice == PriceChoice.Undecided) return;

		while (true)
		{
			var stockPending = Rows.FirstOrDefault(r => r.IsStockUom && r.HasPricePrompt && r.PriceChoice == PriceChoice.Undecided);
			if (stockPending is not null)
			{
				if (scope == PriceScope.OthersOnly) break;
				stockPending.PriceChoice = choice;
				RefreshPricePrompts(preserveChoices: true);
				if (scope == PriceScope.StockOnly) break;
				continue;
			}

			if (scope == PriceScope.StockOnly) break;

			var others = Rows.Where(r => r.HasPricePrompt && r.PriceChoice == PriceChoice.Undecided).ToList();
			if (others.Count == 0) break;

			foreach (var row in others) row.PriceChoice = choice;
			RefreshPricePrompts(preserveChoices: true);
			break;
		}
	}

	public List<string> StockUomOptions => Rows
		.Where(r => !string.IsNullOrWhiteSpace(r.UomName) && r.Conversion is > 0 && r.Price.HasValue)
		.Select(r => r.UomName.Trim())
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToList();

	public string? ChooseStockUom(string uomName)
	{
		var match = StockUomOptions.FirstOrDefault(o => string.Equals(o, uomName?.Trim(), StringComparison.OrdinalIgnoreCase));
		if (match is null)
			return "A stock UOM needs a conversion and a price.";

		// Confirming the stock UOM already in use keeps the price decisions. Changing it resets them.
		var sameStock = string.Equals(StockUomName, match, StringComparison.OrdinalIgnoreCase);

		foreach (var row in Rows)
		{
			row.IsStockUom = string.Equals(row.UomName?.Trim(), match, StringComparison.OrdinalIgnoreCase);
			row.Warnings.RemoveAll(w => w.StartsWith("Stock UOM", StringComparison.OrdinalIgnoreCase));
		}

		StockUomName = match;
		StockUomSource = StockUomSource.Chosen;
		StockUomReason = null;

		RefreshPricePrompts(preserveChoices: sameStock);
		return null;
	}

	public PreparedItemGroup() { }

	public PreparedItemGroup(List<ImportMapItemRowResult> rows)
	{
		Rows = rows ?? new List<ImportMapItemRowResult>();
	}
}

public sealed record ImportMapItemIssue(int RowNumber, string SubdItemCode, string Message);
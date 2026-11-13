namespace STTproject.Models
{
    public class InputItemModel
    {
        public string ItemCode { get; set; } = string.Empty;
        public int SubdItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int ItemsUomId { get; set; }
        public string UomName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
        public int LineItemId { get; set; }
        public int? SourceRowNumber { get; set; }

        public int? SourceSheetIndex { get; set; }
        public List<SourceRowKey> SourceRows { get; set; } = new();
        public List<int>? LineItemIds { get; set; } = new();
        public string? PriceIssue { get; set; }
        public bool IsFreeItem { get; set; }
        public decimal? BilledAmount { get; set; }
        public string? PriceSource { get; set; }

    }
}
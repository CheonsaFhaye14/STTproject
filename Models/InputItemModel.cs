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

        /// <summary>
        /// Row number on its sheet. Only meaningful together with <see cref="SourceSheetIndex"/>; kept for older callers.
        /// Use <see cref="SourceRows"/> for the full, sheet-aware source.
        /// </summary>
        public int? SourceRowNumber { get; set; }

        /// <summary>The one sheet every source row is on, or null when the item's rows span several sheets.</summary>
        public int? SourceSheetIndex { get; set; }

        /// <summary>Every source row that contributed to this item, after aggregation.</summary>
        public List<SourceRowKey> SourceRows { get; set; } = new();
        public List<int>? LineItemIds { get; set; } = new();
    }
}
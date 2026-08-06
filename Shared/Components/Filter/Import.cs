
namespace STTproject.Shared.Components.Filter
{
    public enum ImportGroupFilter { All, SuccessOnly, ErrorOnly }
    public interface IImportRow
    {
        int RowNumber { get; }
        IReadOnlyList<string> Issues { get; }
    }
    public interface IImportGroup<TRow> where TRow : IImportRow
    {
        string GroupKey { get; }
        string DisplayName { get; }
        List<TRow> Rows { get; }
        IReadOnlyList<string> Issues { get; }
        bool Selected { get; set; }
        bool IsSaved { get; }
    }
    public class FilterToggleOption<TValue>
    {
        public required TValue Value { get; set; }
        public required string Label { get; set; }
        public string ActiveClass { get; set; } = "btn-primary";
        public string InactiveClass { get; set; } = "btn-outline-primary";
        public bool Visible { get; set; } = true;
    }
    
};
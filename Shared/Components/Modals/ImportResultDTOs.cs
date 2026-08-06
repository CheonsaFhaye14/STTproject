namespace STTproject.Shared.Components.Modals;
public class ImportResultDTOs
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool HasErrors => Errors.Count > 0;
}
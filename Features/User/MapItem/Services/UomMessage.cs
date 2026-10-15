namespace STTproject.Features.User.MapItem.DTOs;

public enum UomMessageSeverity
{
    Error = 0,
    Warning = 1,
    Note = 2
}
public sealed record UomMessage(
    string Key,
    UomMessageSeverity Severity,
    string Text,
    string? RowName = null,
    string? Field = null,
    string? TargetRow = null)
{
    public string? JumpRow => TargetRow ?? RowName;
}
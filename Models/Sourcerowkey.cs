namespace STTproject.Models;

public readonly record struct SourceRowKey(int SheetIndex, int RowNumber)
{
    public bool IsFileLevel => SheetIndex == 0;
}
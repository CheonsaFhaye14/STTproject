namespace STTproject.Models;

public sealed class InvoiceItemsDraftState
{
    public InputItemModel? NewItem { get; set; }
    public List<InputItemModel> ModalItems { get; set; } = new();
}

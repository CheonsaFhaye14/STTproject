using STTproject.Models;

namespace STTproject.Components.Pages.SalesInvoice;

public sealed class InvoiceItemsDraftState
{
    public InputItemModel? NewItem { get; set; }
    public List<InputItemModel> ModalItems { get; set; } = new();
}

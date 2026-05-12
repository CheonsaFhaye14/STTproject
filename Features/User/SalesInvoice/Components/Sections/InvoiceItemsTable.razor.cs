using Microsoft.AspNetCore.Components;
using STTproject.Models;
using STTproject.Data;
namespace STTproject.Features.User.SalesInvoice.Components.Sections;

public partial class InvoiceItemsTable
{
    [Parameter] public List<InputItemModel> Items { get; set; } = new();
    [Parameter] public EventCallback<List<InputItemModel>> ItemsChanged { get; set; }
    [Parameter] public List<SubdItem> AvailableItems { get; set; } = new();
    [Parameter] public List<ItemsUom> AvailableUoms { get; set; } = new();

    private decimal CalculateTotalLineAmount()
    {
        return Items.Sum(i => i.Amount);
    }

    private string GetUomName(InputItemModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.UomName))
        {
            return item.UomName;
        }

        return AvailableUoms.FirstOrDefault(u => u.ItemsUomId == item.ItemsUomId)?.UomName ?? string.Empty;
    }

}


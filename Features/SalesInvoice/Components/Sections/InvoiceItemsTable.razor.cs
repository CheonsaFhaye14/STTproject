using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using STTproject.Models;
using STTproject.Models.Tables;

namespace STTproject.Features.SalesInvoice.Components.Sections;

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

    private async Task RemoveItem(InputItemModel item)
    {
        Items.Remove(item);
        await ItemsChanged.InvokeAsync(Items);
    }

}


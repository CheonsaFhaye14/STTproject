
﻿using Microsoft.AspNetCore.Components;
using STTproject.Features.User.MapItem.DTOs;
namespace STTproject.Features.User.MapItem.Components.Modals;
public partial class ImportDetails
{
    [Parameter]
    public bool Show { get; set; }

    [Parameter]
    public ImportMapItemResult? ImportResult { get; set; }

    [Parameter]
    public EventCallback OnCommit { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    private bool CanCommit => ImportResult != null && ImportResult.PreparedMapItems != null && ImportResult.PreparedMapItems.Any(p => p.Selected && (p.Issues == null || p.Issues.Count == 0) && !p.IsSaved);

    private List<MapItemViewModel> GetMapItemCards()
    {
        if (ImportResult == null)
        {
            return new List<MapItemViewModel>();
        }

        var preparedBySubdItem = (ImportResult.PreparedMapItems ?? new List<PreparedMapItem>())
            .GroupBy(prepared => NormalizeSubdItemCode(prepared.SubdItemCode))
            .ToDictionary(group => group.Key, group => group.First());

        var issuesByMapItem = ImportResult.Issues
            .Concat((ImportResult.PreparedMapItems ?? new List<PreparedMapItem>()).SelectMany(prepared => prepared.Issues ?? new List<ImportMapItemIssue>()))
            .GroupBy(issue => NormalizeSubdItemCode(issue.SubdItemCode))
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(issue => new { issue.RowNumber, issue.SubdItemCode, issue.ColumnName, issue.Message })
                    .Select(issueGroup => issueGroup.First())
                    .OrderBy(issue => issue.RowNumber)
                    .ThenBy(issue => issue.ColumnName)
                    .ThenBy(issue => issue.Message)
                    .ToList());

        var subdItemKeys = preparedBySubdItem.Keys
            .Concat(issuesByMapItem.Keys)
            .Distinct()
            .OrderBy(key => preparedBySubdItem.TryGetValue(key, out var prepared) ? prepared.SubdItemCode : issuesByMapItem[key].FirstOrDefault()?.SubdItemCode ?? key)
            .ToList();

        var cards = new List<MapItemCardViewModel>();
        foreach (var subdItemKey in subdItemKeys)
        {
            preparedBySubdItem.TryGetValue(subdItemKey, out var prepared);
            issuesByMapItem.TryGetValue(subdItemKey, out var issues);

            cards.Add(new MapItemCardViewModel(
                prepared?.SubdItemCode ?? issues?.FirstOrDefault()?.SubdItemCode ?? subdItemKey,
                prepared,
                issues ?? new List<ImportMapItemIssue>()));
        }

        return cards
            .OrderBy(card => card.Issues.Count > 0 ? card.Issues.Min(issue => issue.RowNumber) : int.MaxValue)
            .ThenBy(card => card.SubdItemCode)
            .ToList();
    }

    private static string NormalizeSubdItemCode(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "[Header Issue]" : value.Trim();
    }
    private static string GetErrorBadgeText(MapItemCardViewModel card)
    {
        var errorCount = card.Issues.Count > 0
            ? card.Issues.Count
            : card.Prepared?.Issues?.Count ?? 0;

        return errorCount == 1
            ? "1 error"
            : $"{errorCount} errors";
    }
    private sealed record MapItemCardViewModel(
        string SubdItemCode,
        PreparedMapItem? Prepared,
        List<ImportMapItemIssue> Issues)
    {
        public bool HasErrors => Issues.Count > 0 || (Prepared?.Issues?.Count ?? 0) > 0;
    }

    private async Task Commit()
    {
        if (OnCommit.HasDelegate)
        {
            await OnCommit.InvokeAsync(null);
        }
    }
}
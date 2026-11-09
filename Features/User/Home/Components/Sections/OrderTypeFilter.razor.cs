using Microsoft.AspNetCore.Components;

namespace STTproject.Features.User.Home.Components.Sections
{
    /// <summary>
    /// Order type dropdown (All / Invoice / Credit) built on the shared Select.
    /// Empty value = all order types (the placeholder).
    /// Give it a @key from the parent's filterResetKey so "Clear filters" resets it.
    /// </summary>
    public partial class OrderTypeFilter
    {
        public sealed record OrderTypeOption(string Value, string Text);

        private static readonly List<OrderTypeOption> Options = new()
        {
            new("invoice", "Invoice"),
            new("credit", "Credit")
        };

        [Parameter] public string Label { get; set; } = "Order type";

        [Parameter] public string Value { get; set; } = string.Empty;

        [Parameter] public EventCallback<string> ValueChanged { get; set; }

        private Task OnSelected(string? value) => ValueChanged.InvokeAsync(value ?? string.Empty);
    }
}
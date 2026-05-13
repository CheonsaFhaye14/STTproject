using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using STTproject.Shared.Components;
using STTproject.Features.User.MapItem.DTOs;

namespace STTproject.Features.User.MapItem.Components.Sections;

public partial class MapItemInputHeader
{
    [Inject] public IJSRuntime JSRuntime { get; set; } = default!;

    [Parameter] public bool IsSubDistributorSelected { get; set; }
    [Parameter] public bool IsEditingItem { get; set; }
    [Parameter] public bool CanOpenAddUom { get; set; }
    [Parameter] public bool CanSaveItem { get; set; }
    [Parameter] public string? ItemCode { get; set; }
    [Parameter] public EventCallback<string?> ItemCodeChanged { get; set; }
    [Parameter] public EventCallback OnItemCodeChanged { get; set; }
    [Parameter] public string? ItemName { get; set; }
    [Parameter] public EventCallback<string?> ItemNameChanged { get; set; }
    [Parameter] public EventCallback OnItemNameChanged { get; set; }
    [Parameter] public string? SelectedDropdownPrincipal { get; set; }
    [Parameter] public EventCallback<string?> SelectedDropdownPrincipalChanged { get; set; }
    [Parameter] public EventCallback OnPrincipalChanged { get; set; }
    [Parameter] public IEnumerable<string> DisplayPrincipalsDropdown { get; set; } = Array.Empty<string>();
    [Parameter] public IReadOnlyList<CompanyItemDropdownItem> CompanyItemsForDropdown { get; set; } = Array.Empty<CompanyItemDropdownItem>();
    [Parameter] public string? SelectedCompanyItemDisplayName { get; set; }
    [Parameter] public EventCallback<string?> SelectedCompanyItemDisplayNameChanged { get; set; }
    [Parameter] public EventCallback<string?> OnCompanyItemValueChanged { get; set; }
    [Parameter] public EventCallback<CompanyItemDropdownItem?> OnAutocompleteSelected { get; set; }
    [Parameter] public EventCallback OnCompanyItemConfirmed { get; set; }
    [Parameter] public Func<string, string?> GetFieldError { get; set; } = default!;
    [Parameter] public Func<string> GetUnitOfMeasureSummary { get; set; } = default!;
    [Parameter] public EventCallback<FocusEventArgs> OnFormFieldBlur { get; set; }
    [Parameter] public EventCallback<KeyboardEventArgs> OnItemCodeKeyDown { get; set; }
    [Parameter] public EventCallback<KeyboardEventArgs> OnItemNameKeyDown { get; set; }
    [Parameter] public EventCallback<KeyboardEventArgs> OnPrincipalKeyDown { get; set; }
    [Parameter] public EventCallback OnOpenAddUomModal { get; set; }
    [Parameter] public EventCallback OnSaveItem { get; set; }
    [Parameter] public EventCallback OnClearDraftRequested { get; set; }
    [Parameter] public EventCallback OnCancelEdit { get; set; }

    private ElementReference itemCodeInput;
    private ElementReference itemNameInput;
    private ElementReference principalSelect;
    private GenericAutocomplete<CompanyItemDropdownItem>? companyItemAutocomplete;
    private ElementReference addUomButton;
    private ElementReference saveButton;
    private IJSObjectReference? jsModule;

    private string? itemCodeInternal;
    private string? itemNameInternal;

    protected override void OnParametersSet()
    {
        if (ItemCode != itemCodeInternal)
        {
            itemCodeInternal = ItemCode;
        }

        if (ItemName != itemNameInternal)
        {
            itemNameInternal = ItemName;
        }
    }

    private async Task HandleItemCodeInput(ChangeEventArgs args)
    {
        itemCodeInternal = args.Value?.ToString();
        await ItemCodeChanged.InvokeAsync(itemCodeInternal);

        if (OnItemCodeChanged.HasDelegate)
        {
            await OnItemCodeChanged.InvokeAsync();
        }
    }

    private async Task HandleItemNameInput(ChangeEventArgs args)
    {
        itemNameInternal = args.Value?.ToString();
        await ItemNameChanged.InvokeAsync(itemNameInternal);

        if (OnItemNameChanged.HasDelegate)
        {
            await OnItemNameChanged.InvokeAsync();
        }
    }

    private async Task HandlePrincipalChanged(ChangeEventArgs args)
    {
        SelectedDropdownPrincipal = args.Value?.ToString();
        await SelectedDropdownPrincipalChanged.InvokeAsync(SelectedDropdownPrincipal);

        if (OnPrincipalChanged.HasDelegate)
        {
            await OnPrincipalChanged.InvokeAsync();
        }
    }

    private async Task HandleCompanyItemValueChanged(string? value)
    {
        SelectedCompanyItemDisplayName = value;
        await SelectedCompanyItemDisplayNameChanged.InvokeAsync(value);

        if (OnCompanyItemValueChanged.HasDelegate)
        {
            await OnCompanyItemValueChanged.InvokeAsync(value);
        }
    }

    public Task FocusItemCodeAsync() => itemCodeInput.FocusAsync().AsTask();

    public Task FocusItemNameAsync() => itemNameInput.FocusAsync().AsTask();

    public async Task FocusPrincipalAsync()
    {
        await principalSelect.FocusAsync();
    }

    public async Task OpenPrincipalDropdownAsync()
    {
        jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
        await jsModule.InvokeVoidAsync("openSelectDropdown", principalSelect);
    }

    public Task FocusCompanyItemAsync()
    {
        return companyItemAutocomplete is null
            ? Task.CompletedTask
            : companyItemAutocomplete.OpenPopupAsync();
    }

    public static string GetCompanyItemLabel(CompanyItemDropdownItem item) =>
        string.IsNullOrWhiteSpace(item.ItemCode)
            ? item.ItemName
            : $"({item.ItemCode}) {item.ItemName}";

    public Task FocusAddUomButtonAsync() => addUomButton.FocusAsync().AsTask();

    public Task FocusSaveButtonAsync() => saveButton.FocusAsync().AsTask();
}
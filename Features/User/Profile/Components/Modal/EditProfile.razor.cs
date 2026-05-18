using Microsoft.AspNetCore.Components;

namespace STTproject.Features.User.Profile.Components.Modal;

public partial class EditProfile
{
    [Parameter]
    public STTproject.Data.User? EditingUser { get; set; }

    [Parameter]
    public EventCallback OnSave { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    public bool IsSaving { get; set; } = false;
    public bool ShowConfirmationModal { get; set; } = false;

    private async Task ShowConfirmationAsync()
    {
        ShowConfirmationModal = true;
    }

    private void CloseConfirmation()
    {
        ShowConfirmationModal = false;
    }

    private async Task OnSaveClick()
    {
        IsSaving = true;
        await OnSave.InvokeAsync();
        IsSaving = false;
    }

    private async Task OnCancelClick()
    {
        await OnCancel.InvokeAsync();
    }
}

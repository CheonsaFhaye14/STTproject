using STTproject.Features.Admin.PriceIncrease.DTOs;
using STTproject.Features.Admin.PriceIncrease.Services;

namespace STTproject.Features.Admin.PriceIncrease.Validators;

public static class PriceIncreaseValidations
{
    public static class AddPriceIncrease
    {
        public static readonly PriceIncreaseField CompanyItem = new(nameof(CompanyItem), "Company Item", true, "Company item is required.");
        public static readonly PriceIncreaseField PriceIncreaseAmount = new(nameof(PriceIncreaseAmount), "Price Adjustment Amount", true, "Price adjustment amount is required.");
        public static readonly PriceIncreaseField EffectivityDate = new(nameof(EffectivityDate), "Effectivity Date", true, "Effectivity date is required.");
    }

    public static string Label(PriceIncreaseField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateAddPriceIncreaseAsync(
        AddPriceIncreaseDto dto,
        IAdminPriceIncreaseService service)
    {
        var errors = new Dictionary<string, string>();

        if (!dto.CompanyItemId.HasValue || dto.CompanyItemId.Value <= 0)
        {
            errors[AddPriceIncrease.CompanyItem.Key] = AddPriceIncrease.CompanyItem.ErrorMessage;
        }

        ValidateAmountAndDate(dto.PriceIncreaseAmount, dto.EffectivityDate, errors);

        if (dto.CompanyItemId.HasValue && dto.EffectivityDate.HasValue)
        {
            var (existing, _) = await service.GetPagedAsync(
                page: 1,
                pageSize: 1,
                search: null,
                status: "pending",
                principal: null);

            var hasDuplicatePending = existing.Any(x =>
                x.CompanyItemId == dto.CompanyItemId.Value);

            if (hasDuplicatePending)
            {
                errors[AddPriceIncrease.EffectivityDate.Key] =
                    "This company item already has a pending price change scheduled.";
            }
        }

        return errors;
    }

    public static Task<Dictionary<string, string>> ValidateEditPriceIncreaseAsync(AddPriceIncreaseDto dto)
    {
        var errors = new Dictionary<string, string>();
        ValidateAmountAndDate(dto.PriceIncreaseAmount, dto.EffectivityDate, errors);
        return Task.FromResult(errors);
    }

    private static void ValidateAmountAndDate(
        decimal? priceIncreaseAmount,
        DateTime? effectivityDate,
        Dictionary<string, string> errors)
    {
        if (!priceIncreaseAmount.HasValue)
        {
            errors[AddPriceIncrease.PriceIncreaseAmount.Key] = AddPriceIncrease.PriceIncreaseAmount.ErrorMessage;
        }
        else if (priceIncreaseAmount.Value == 0)
        {
            errors[AddPriceIncrease.PriceIncreaseAmount.Key] = "Amount cannot be zero.";
        }

        if (!effectivityDate.HasValue)
        {
            errors[AddPriceIncrease.EffectivityDate.Key] = AddPriceIncrease.EffectivityDate.ErrorMessage;
        }
        else if (effectivityDate.Value.Date < DateTime.Now.Date)
        {
            errors[AddPriceIncrease.EffectivityDate.Key] = "Effectivity date cannot be in the past.";
        }
    }
}

public sealed record PriceIncreaseField(string Key, string Label, bool Required, string ErrorMessage);
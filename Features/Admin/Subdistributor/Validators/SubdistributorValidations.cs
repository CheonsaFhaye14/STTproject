using System.Text.RegularExpressions;
using STTproject.Data;
using STTproject.Features.Admin.Subdistributor.Services;

namespace STTproject.Features.Admin.Subdistributor.Validators;

public static class SubDistributorValidations
{
    public static class AddSubDistributor
    {
        public static readonly SubDistributorField subdcode = new(nameof(subdcode), "Subdistributor Code", true, "Subdistributor code is required.");
        public static readonly SubDistributorField subdname = new(nameof(subdname), "Subdistributor Name", true, "Subdistributor name is required.");
        public static readonly SubDistributorField citymunicipality = new(nameof(citymunicipality), "City / Municipality", true, "City/Municipality is required.");
        public static readonly SubDistributorField province = new(nameof(province), "Province", true, "Province is required.");
        public static readonly SubDistributorField companysubdcode = new(nameof(companysubdcode), "Company Subdistributor Code", true, "Company subdistributor code is required.");
        public static readonly SubDistributorField encoder = new(nameof(encoder), "Encoder", false, "Selected user is not a valid Encoder.");
    }

    public static string Label(SubDistributorField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateAddSubDistributorAsync(
        Data.SubDistributor subDistributor,
        IAdminSubDistributorService subDistributorService,
        int? excludeId = null
    )
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(subDistributor.SubdCode))
        {
            errors[AddSubDistributor.subdcode.Key] = AddSubDistributor.subdcode.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(subDistributor.SubdName))
        {
            errors[AddSubDistributor.subdname.Key] = AddSubDistributor.subdname.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(subDistributor.CityMunicipality))
        {
            errors[AddSubDistributor.citymunicipality.Key] = AddSubDistributor.citymunicipality.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(subDistributor.Province))
        {
            errors[AddSubDistributor.province.Key] = AddSubDistributor.province.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(subDistributor.CompanySubdCode))
        {
            errors[AddSubDistributor.companysubdcode.Key] = AddSubDistributor.companysubdcode.ErrorMessage;
        }

        // Encoder is optional, but if one is assigned it must actually be an active Encoder-role user.
        if (subDistributor.EncoderId.HasValue)
        {
            var isValidEncoder = await subDistributorService.IsValidEncoderAsync(subDistributor.EncoderId.Value);
            if (!isValidEncoder)
            {
                errors[AddSubDistributor.encoder.Key] = AddSubDistributor.encoder.ErrorMessage;
            }
        }

        return errors;
    }
}

public sealed record SubDistributorField(string Key, string Label, bool Required, string ErrorMessage);
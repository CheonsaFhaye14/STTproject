using System.Text.RegularExpressions;
using STTproject.Data;
using STTproject.Features.Admin.Subdistributor.Services;

namespace STTproject.Features.Admin.Subdistributor.Validators;

public static class SubDistributorValidations
{
    public static class AddSubDistributor
    {
        public static readonly SubDistributorField subdcode = new(nameof(subdcode), "Subdistributor Code", true, "Subdistributor code is required.");
        public static readonly SubDistributorField subdname = new(nameof(subdname), "Subdistributor Name", true, "Subdistributor name is required.");        public static readonly SubDistributorField encoder = new(nameof(encoder), "Encoder", false, "Selected user is not a valid Encoder.");
    }

    public static string Label(SubDistributorField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateAddSubDistributorAsync(
        SubDistributor subDistributor
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

        return errors;
    }
}

public sealed record SubDistributorField(string Key, string Label, bool Required, string ErrorMessage);
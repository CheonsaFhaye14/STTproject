namespace STTproject.Features.Admin.Customers.Services;

// One place for the rule the migration used, so everything the app saves or looks up
// matches what is stored in SubdCustomer:
//   - values are trimmed
//   - a missing value is "" (never NULL)
//   - the placeholder "NoSubdCustomerCode" means "no code" and is stored as ""
public static class SubdCustomerKey
{
    public const string NoCodePlaceholder = "NoSubdCustomerCode";
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;

    public static string Code(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        return string.Equals(v, NoCodePlaceholder, StringComparison.OrdinalIgnoreCase) ? string.Empty : v;
    }

    public static string Name(string? value) => (value ?? string.Empty).Trim();

    public static bool IsBlank(string? code, string? name) =>
        Code(code).Length == 0 && Name(name).Length == 0;

    // In-memory comparison key. SQL comparisons are already case-insensitive (default collation).
    public static string Compare(string? code, string? name) =>
        $"{Code(code)}|{Name(name)}".ToUpperInvariant();
}
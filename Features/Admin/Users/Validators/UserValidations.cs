using System.Text.RegularExpressions;
using STTproject.Data;

namespace STTproject.Features.Admin.Users.Validators;

public static class UserValidations
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static class AddUser
    {
        public static readonly UserField fullname = new(nameof(fullname), "Full Name", true, "Full name is required.");
        public static readonly UserField username = new(nameof(username), "Username", true, "Username is required.");
        public static readonly UserField email = new(nameof(email), "Email", true, "Email is required.");
        public static readonly UserField role = new(nameof(role), "Role", true, "Role is required.");
        public static readonly UserField password = new(nameof(password), "Password", true, "Password is required.");
    }

    public static string Label(UserField field)
    {
        return field.Required ? $"{field.Label} *" : field.Label;
    }

    public static async Task<Dictionary<string, string>> ValidateAddUserAsync(
        Data.User user
    )
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(user.FullName))
        {
            errors[AddUser.fullname.Key] = AddUser.fullname.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(user.Username))
        {
            errors[AddUser.username.Key] = AddUser.username.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            errors[AddUser.email.Key] = AddUser.email.ErrorMessage;
        }
        else if (!EmailRegex.IsMatch(user.Email.Trim()))
        {
            errors[AddUser.email.Key] = "Please enter a valid email address.";
        }

        if (string.IsNullOrWhiteSpace(user.Role))
        {
            errors[AddUser.role.Key] = AddUser.role.ErrorMessage;
        }
        return errors;
    }
}


public sealed record UserField(string Key, string Label, bool Required, string ErrorMessage);
using STTproject.Data;

namespace STTproject.Features.Admin.Users.Validators;

public static class UserValidations
{
    public static class AddUser
    {
        public static readonly UserField fullname = new(nameof(fullname), "Full Name", true, "Full name is required.");
        public static readonly UserField username = new(nameof(username), "Username", true, "Username is required.");
        public static readonly UserField email = new(nameof(email), "Email", true, "Email is required.");
        public static readonly UserField role = new(nameof(role), "Role", true, "Role is required.");
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

        if (string.IsNullOrWhiteSpace(user.Username))
        {
            errors[AddUser.username.Key] = AddUser.username.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            errors[AddUser.email.Key] = AddUser.email.ErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(user.Role))
        {
            errors[AddUser.role.Key] = AddUser.role.ErrorMessage;
        }
        return errors;
    }
}


public sealed record UserField(string Key, string Label, bool Required, string ErrorMessage);

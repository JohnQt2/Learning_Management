using System.ComponentModel.DataAnnotations;

namespace LMS.CoreApplicationServices.Common;

public static class ProfileValidation
{
    public static string? NormalizeEmail(string? value)
    {
        var email = value?.Trim();
        if (string.IsNullOrEmpty(email)) return null;
        if (email.Length > 254 || email.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(email))
            throw new AppException(400, "Email không hợp lệ (tối đa 254 ký tự)");
        return email;
    }
}

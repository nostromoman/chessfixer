using ChessFixer.Server.Errors;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Services.Auth;

/// <summary>Validation of account data.</summary>
public static class UserRules
{
    public const int MaxUserNameLength = 50;
    public const int MaxDisplayNameLength = 100;

    public static string ValidateUserName(string? userName)
    {
        var trimmed = userName?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > MaxUserNameLength) throw new ApiException(ApiErrorCode.UserNameRequired);
        return trimmed;
    }

    public static string ValidateDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > MaxDisplayNameLength) throw new ApiException(ApiErrorCode.DisplayNameRequired);
        return trimmed;
    }

    /// <summary>Passwords are not trimmed: spaces are allowed and count.</summary>
    public static string ValidatePassword(string? password)
    {
        if (password is null || password.Length < PasswordRules.MinLength || password.Length > PasswordRules.MaxLength)
            throw new ApiException(ApiErrorCode.PasswordTooShort);
        return password;
    }
}

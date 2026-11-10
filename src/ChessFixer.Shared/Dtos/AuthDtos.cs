using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

public record LoginRequest(string UserName, string Password);

/// <summary>The logged-in user.</summary>
public record CurrentUserDto(int Id, string UserName, string DisplayName, UserRole Role);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>A tournament leader account (managed by the system administrator).</summary>
public record LeaderDto(int Id, string UserName, string DisplayName);

public record CreateLeaderRequest(string UserName, string DisplayName, string Password);

/// <param name="Password">Null or empty keeps the current password; a new password logs the leader out everywhere.</param>
public record UpdateLeaderRequest(string UserName, string DisplayName, string? Password = null);

public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
}

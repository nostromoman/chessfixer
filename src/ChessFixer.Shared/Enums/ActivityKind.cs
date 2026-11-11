using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>What a user did (activity log, only visible to the system administrator).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActivityKind>))]
public enum ActivityKind
{
    Login,
    /// <summary>Wrong user name or password. The entry has the user name that was typed.</summary>
    LoginFailed,
    Logout,
    PasswordChanged,
    /// <summary>A change through the API (create, change, delete, start a round, enter a result, ...).</summary>
    Action
}

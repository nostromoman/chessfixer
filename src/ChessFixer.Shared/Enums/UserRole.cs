using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>
/// Who logs in. Players never log in; only tournament leaders and the system administrator.
/// The name is also the role claim, so it can be used in [Authorize(Roles = nameof(UserRole.Admin))].
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
public enum UserRole
{
    /// <summary>Runs tournaments and manages players and settings; cannot manage other tournament leaders.</summary>
    TournamentLeader,

    /// <summary>System administrator: may do everything, including managing the tournament leaders.</summary>
    Admin
}

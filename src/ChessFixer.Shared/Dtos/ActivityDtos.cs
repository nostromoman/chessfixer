using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <summary>One entry of the activity log.</summary>
/// <param name="UserId">Null for a failed login with an unknown user name.</param>
/// <param name="Action">For <see cref="ActivityKind.Action"/>: controller and action, e.g. "Tournaments.StartNextRound".</param>
/// <param name="StatusCode">For <see cref="ActivityKind.Action"/>: the HTTP status of the answer (400 and up: refused or failed).</param>
/// <param name="TournamentId">The tournament the action was about, when it still exists.</param>
public record ActivityLogEntryDto(
    long Id, DateTime At, int? UserId, string UserName, string? DisplayName, ActivityKind Kind,
    string? Action, string? Method, string? Path, int? StatusCode, string? IpAddress, string? UserAgent,
    int? TournamentId, string? TournamentTitle);

public record ActivityLogPageDto(int Total, IReadOnlyList<ActivityLogEntryDto> Entries);

/// <summary>A user account with a summary of its activity.</summary>
/// <param name="LastSeenAt">The last request of the user (also just looking), to the minute.</param>
/// <param name="FailedActions">Actions that the server refused or that failed.</param>
public record ActivityUserDto(
    int Id, string UserName, string DisplayName, UserRole Role,
    DateTime? LastLoginAt, DateTime? LastSeenAt, int Logins, int FailedLogins, int Actions, int FailedActions);

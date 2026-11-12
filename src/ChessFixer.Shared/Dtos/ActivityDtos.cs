using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <summary>One entry of the activity log.</summary>
/// <param name="UserId">Null for a failed login with an unknown user name.</param>
/// <param name="Action">For <see cref="ActivityKind.Action"/>: controller and action, e.g. "Tournaments.StartNextRound".</param>
/// <param name="StatusCode">For <see cref="ActivityKind.Action"/>: the HTTP status of the answer (400 and up: refused or failed).</param>
/// <param name="TournamentId">The tournament the action was about, when it still exists.</param>
/// <param name="Changes">What a successful action changed in the database.</param>
/// <param name="CanRevert">The administrator can try to revert this action (it may still turn out to be changed since).</param>
/// <param name="RevertedBy">The revert of this action, when it was reverted.</param>
/// <param name="RevertOfId">For a revert: the entry it reverted.</param>
public record ActivityLogEntryDto(
    long Id, DateTime At, int? UserId, string UserName, string? DisplayName, ActivityKind Kind,
    string? Action, string? Method, string? Path, int? StatusCode, string? IpAddress, string? UserAgent,
    int? TournamentId, string? TournamentTitle,
    IReadOnlyList<ActivityChangeDto> Changes, bool CanRevert, ActivityRevertDto? RevertedBy, long? RevertOfId);

/// <summary>One row that an action added, changed or deleted.</summary>
/// <param name="EntityType">The kind of row: "Player", "Tournament", "Game", ...</param>
/// <param name="Label">A readable name of the row (player name, tournament title, ...), when known.</param>
/// <param name="Properties">Added: the values filled in. Modified: only what changed. Deleted: the values it had.</param>
public record ActivityChangeDto(
    string EntityType, string EntityKey, string? Label, ActivityChangeKind Kind, IReadOnlyList<ActivityPropertyChangeDto> Properties);

/// <summary>A property with its value before and after, as invariant text (dates ISO 8601, enums by name).</summary>
/// <param name="IsMasked">A secret or photo: only that it changed is known, not the values.</param>
public record ActivityPropertyChangeDto(string Name, string? Before, string? After, bool IsMasked = false);

/// <summary>Who reverted an action, and when.</summary>
public record ActivityRevertDto(long EntryId, DateTime At, string UserName, string? DisplayName);

public record ActivityLogPageDto(int Total, IReadOnlyList<ActivityLogEntryDto> Entries);

/// <summary>A user account with a summary of its activity.</summary>
/// <param name="LastSeenAt">The last request of the user (also just looking), to the minute.</param>
/// <param name="FailedActions">Actions that the server refused or that failed.</param>
public record ActivityUserDto(
    int Id, string UserName, string DisplayName, UserRole Role,
    DateTime? LastLoginAt, DateTime? LastSeenAt, int Logins, int FailedLogins, int Actions, int FailedActions);

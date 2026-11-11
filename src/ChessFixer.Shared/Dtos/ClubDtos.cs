namespace ChessFixer.Shared.Dtos;

/// <param name="IsOwnClub">The club this app is used by; at most one club is the own club.</param>
/// <param name="GuestPlayers">Number of (not deleted) guest players of this club.</param>
public record ClubDto(int Id, string Name, string? City, string? Description, bool IsOwnClub, int GuestPlayers = 0);

public record SaveClubRequest(string Name, string? City, string? Description, bool IsOwnClub);

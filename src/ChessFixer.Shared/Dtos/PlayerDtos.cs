namespace ChessFixer.Shared.Dtos;

/// <param name="Name">Full name (first and last name), for display.</param>
/// <param name="PhotoVersion">Null when the player has no photo; otherwise changes with every new photo.</param>
public record PlayerDto(
    int Id, string FirstName, string LastName, string Name, int? Rating,
    DateOnly? BirthDate, string? City, string? Club, int? PhotoVersion)
{
    /// <summary>URL of the profile photo, or null when there is none.</summary>
    public string? PhotoUrl => PhotoVersion is { } version ? $"api/players/{Id}/photo?v={version}" : null;
}

public record CreatePlayerRequest(
    string FirstName, string? LastName, int? Rating,
    DateOnly? BirthDate = null, string? City = null, string? Club = null);

public record UpdatePlayerRequest(
    string FirstName, string? LastName, int? Rating,
    DateOnly? BirthDate = null, string? City = null, string? Club = null);

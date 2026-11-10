using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <param name="Name">Full name (first and last name), for display.</param>
/// <param name="PhotoVersion">Null when the player has no photo; otherwise changes with every new photo.</param>
/// <param name="IsDeleted">Removed from the player list; only returned when deleted players are asked for.</param>
public record PlayerDto(
    int Id, string FirstName, string LastName, string Name, int Rating,
    DateOnly? BirthDate, string? City, string? Club, int? PhotoVersion, bool IsDeleted = false)
{
    /// <summary>URL of the profile photo, or null when there is none.</summary>
    public string? PhotoUrl => PhotoVersion is { } version ? $"api/players/{Id}/photo?v={version}" : null;
}

/// <param name="Rating">Null gives the default rating (1200).</param>
public record CreatePlayerRequest(
    string FirstName, string? LastName, int? Rating,
    DateOnly? BirthDate = null, string? City = null, string? Club = null);

/// <param name="Rating">A new rating set by hand; null keeps the current rating.</param>
public record UpdatePlayerRequest(
    string FirstName, string? LastName, int? Rating,
    DateOnly? BirthDate = null, string? City = null, string? Club = null);

/// <summary>A player's results over all tournaments they played in. Totals only count games with a result.</summary>
public record PlayerStatisticsDto(
    int Tournaments,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    IReadOnlyList<PlayerTournamentResultDto> TournamentResults,
    IReadOnlyList<PlayerGameDto> Games);

/// <summary>The player's place in one tournament (newest tournament first).</summary>
/// <param name="PlayerCount">Number of players in the standings of that tournament.</param>
public record PlayerTournamentResultDto(
    int TournamentId,
    string Title,
    TournamentState State,
    int Rank,
    int PlayerCount,
    double Points,
    int Played,
    int Wins,
    int Draws,
    int Losses);

/// <summary>One game of the player in a started round (newest first).</summary>
/// <param name="Opponent">Null for a bye.</param>
/// <param name="Points">Points the player got for this game; null while the game has no result.</param>
public record PlayerGameDto(
    int TournamentId,
    string TournamentTitle,
    int Round,
    bool IsWhite,
    GamePlayerDto? Opponent,
    GameResult Result,
    double? Points);

/// <summary>The player's current rating and every rated game, oldest first.</summary>
/// <param name="RatedGames">Number of rated games; with fewer than 30 the rating still changes faster (K = 40).</param>
public record PlayerRatingDto(int Rating, int RatedGames, IReadOnlyList<RatingChangeDto> History);

/// <summary>The rating change of one game.</summary>
public record RatingChangeDto(
    DateTime Date,
    int TournamentId,
    string TournamentTitle,
    int Round,
    bool IsWhite,
    GamePlayerDto Opponent,
    int OpponentRating,
    GameResult Result,
    int RatingBefore,
    int Change)
{
    public int RatingAfter => RatingBefore + Change;
}

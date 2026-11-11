using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <param name="Name">Full name (first and last name), for display.</param>
/// <param name="PhotoVersion">Null when the player has no photo; otherwise changes with every new photo.</param>
/// <param name="IsDeleted">Removed from the player list; only returned when deleted players are asked for.</param>
/// <param name="IsGuest">A guest player (not a member of the own club). Games with a guest are not rated.</param>
/// <param name="ClubId">The club of a guest player; null when unknown.</param>
public record PlayerDto(
    int Id, string FirstName, string LastName, string Name, int Rating,
    DateOnly? BirthDate, string? City, int? PhotoVersion, bool IsDeleted = false,
    bool IsGuest = false, int? ClubId = null, string? ClubName = null)
{
    /// <summary>URL of the profile photo, or null when there is none.</summary>
    public string? PhotoUrl => PhotoVersion is { } version ? $"api/players/{Id}/photo?v={version}" : null;
}

/// <param name="Rating">Null gives the default rating (1400).</param>
/// <param name="ClubId">Only for a guest player; ignored otherwise.</param>
public record CreatePlayerRequest(
    string FirstName, string? LastName, int? Rating,
    DateOnly? BirthDate = null, string? City = null, bool IsGuest = false, int? ClubId = null);

/// <param name="Rating">A new rating set by hand; null keeps the current rating.</param>
/// <param name="ClubId">Only for a guest player; ignored otherwise.</param>
public record UpdatePlayerRequest(
    string FirstName, string? LastName, int? Rating,
    DateOnly? BirthDate = null, string? City = null, bool IsGuest = false, int? ClubId = null);

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

/// <summary>The player's rating history: every rating change (for the chart) and every tournament with its games.</summary>
/// <param name="RatedGames">Number of games that changed the rating.</param>
/// <param name="Timeline">The rating after every change, oldest first (from the rating log).</param>
/// <param name="Tournaments">The rated tournaments the player played, newest first, with every game and its change.</param>
public record PlayerRatingDto(
    int Rating,
    int RatedGames,
    IReadOnlyList<RatingPointDto> Timeline,
    IReadOnlyList<RatingTournamentDto> Tournaments);

/// <summary>One rating change in the chart.</summary>
public record RatingPointDto(DateTime At, int RatingBefore, int RatingAfter, RatingChangeReason Reason, string? TournamentTitle)
{
    public int Change => RatingAfter - RatingBefore;
}

/// <summary>A rated tournament of the player.</summary>
/// <param name="IsProcessed">The ratings have been processed (the tournament is closed). Otherwise the changes are
/// what they would be if the tournament were closed now.</param>
/// <param name="RatingBefore">The rating the games were (or will be) calculated with.</param>
/// <param name="ExactChange">The changes of all games added up, before rounding.</param>
/// <param name="Change">The change of the rating: <see cref="ExactChange"/> rounded.</param>
public record RatingTournamentDto(
    int TournamentId,
    string Title,
    DateTime? Date,
    bool IsProcessed,
    int RatingBefore,
    decimal ExactChange,
    int Change,
    IReadOnlyList<RatingGameDto> Games)
{
    public int RatingAfter => RatingBefore + Change;
}

/// <summary>One game of the player in a rated tournament.</summary>
/// <param name="CountsForRating">False for a game against a guest player (and for forfeits, which are not listed).</param>
/// <param name="Rating">The player's rating the game was calculated with.</param>
/// <param name="ExpectedScore">From the FIDE table; null when the game does not count.</param>
/// <param name="K">The K-factor; null when the game does not count or was rated before it was stored.</param>
/// <param name="Change">The exact change of this game; null when the game does not count.</param>
public record RatingGameDto(
    int Round,
    bool IsWhite,
    GamePlayerDto Opponent,
    GameResult Result,
    bool CountsForRating,
    int Rating,
    int OpponentRating,
    decimal Score,
    decimal? ExpectedScore,
    int? K,
    decimal? Change);

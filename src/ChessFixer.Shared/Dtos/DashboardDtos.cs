using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <summary>Everything the home page shows: an overview of the whole system.</summary>
/// <param name="Players">Own players (no guests), not deleted.</param>
/// <param name="GamesPlayed">Games with a result over the board or by forfeit (no byes).</param>
/// <param name="AverageRating">Of the own players; null without players.</param>
/// <param name="GamesPerMonth">The last 12 months, oldest first, including months without games.</param>
/// <param name="TopPlayers">The highest rated own players.</param>
/// <param name="Risers">Own players who gained the most rating in the last 30 days (only gains).</param>
/// <param name="OpenTournaments">Tournaments that are not closed, newest first.</param>
/// <param name="RecentlyClosed">The latest closed tournaments, with their winner.</param>
public record DashboardDto(
    int Tournaments,
    int ClosedTournaments,
    int Players,
    int GuestPlayers,
    int Clubs,
    int GamesPlayed,
    int RatedGames,
    int? AverageRating,
    DashboardResultsDto Results,
    IReadOnlyList<DashboardMonthDto> GamesPerMonth,
    IReadOnlyList<RatingListEntryDto> TopPlayers,
    IReadOnlyList<RatingListEntryDto> Risers,
    IReadOnlyList<DashboardTournamentDto> OpenTournaments,
    IReadOnlyList<DashboardClosedTournamentDto> RecentlyClosed);

/// <summary>How all played games ended.</summary>
public record DashboardResultsDto(int WhiteWins, int Draws, int BlackWins, int Forfeits)
{
    public int Total => WhiteWins + Draws + BlackWins + Forfeits;
}

/// <param name="Month">The first day of the month.</param>
public record DashboardMonthDto(DateOnly Month, int Games);

/// <summary>An open tournament with the progress of its current round.</summary>
/// <param name="Round">The last started round; 0 before round 1.</param>
/// <param name="GamesFinished">Games of that round with a result (no byes).</param>
/// <param name="GamesInRound">Games of that round (no byes).</param>
public record DashboardTournamentDto(
    int Id, string Title, TournamentState State, int Players, int Round, int GamesFinished, int GamesInRound);

/// <param name="Winner">Name of the number one in the standings; null when nobody played.</param>
public record DashboardClosedTournamentDto(int Id, string Title, DateTime ClosedAt, int Players, int Rounds, string? Winner);

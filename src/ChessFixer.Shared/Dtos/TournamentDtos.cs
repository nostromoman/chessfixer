using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

public record TournamentSummaryDto(int Id, string Title, TournamentState State, int PlayerCount, int RoundCount);

/// <param name="RoundsStarted">Number of rounds that have started (including the active one).</param>
/// <param name="SettingsLocked">True once round 1 has started; from then on the pairing system and the points cannot change.
/// Title, time control and boards can still change between rounds.</param>
/// <param name="TotalRounds">Round robin only: the number of rounds needed for everyone to play everyone.</param>
public record TournamentDto(
    int Id,
    string Title,
    TournamentState State,
    TournamentSettingsDto Settings,
    int RoundsStarted,
    bool SettingsLocked,
    int? TotalRounds);

public record TournamentSettingsDto(
    PairingSystem PairingSystem,
    int GameTimeMinutes,
    int IncrementSeconds,
    double PointsForWin,
    double PointsForDraw,
    double PointsForLoss,
    double PointsForBye,
    int Boards);

public record CreateTournamentRequest(string Title);

public record UpdateTournamentRequest(string Title, TournamentSettingsDto Settings);

/// <summary>A player in a tournament. PlayerId refers to the central player list.</summary>
public record TournamentPlayerDto(int PlayerId, string Name, int? Rating, bool IsActive);

public record AddTournamentPlayerRequest(int PlayerId);

/// <param name="IsStarted">False for the next round that is paired but not started yet.</param>
public record RoundDto(int Number, bool IsStarted, IReadOnlyList<GameDto> Games);

/// <param name="Number">Order of the game in the pairing; queued games get a board in this order.</param>
/// <param name="Board">The board the game is (or was) played on; null while it waits in the queue, and for a bye.</param>
/// <param name="Black">Null when White has a bye.</param>
public record GameDto(int Id, int Number, int? Board, GamePlayerDto White, GamePlayerDto? Black, GameResult Result);

public record GamePlayerDto(int PlayerId, string Name);

public record SetResultRequest(GameResult Result);

public record StandingDto(
    int Rank,
    int PlayerId,
    string Name,
    bool IsActive,
    double Points,
    double Buchholz,
    double SonnebornBerger,
    int Played,
    int Wins,
    int Draws,
    int Losses);

public record ApiError(ApiErrorCode Code);

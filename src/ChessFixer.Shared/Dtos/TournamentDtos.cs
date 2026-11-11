using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <param name="PlayerCount">Number of active players.</param>
public record TournamentSummaryDto(
    int Id,
    string Title,
    TournamentState State,
    PairingSystem PairingSystem,
    int PlayerCount,
    int Boards,
    int GameTimeMinutes,
    int IncrementSeconds,
    int RoundCount);

/// <param name="RoundsStarted">Number of rounds that have started (including the active one).</param>
/// <param name="SettingsLocked">True once round 1 has started; from then on the pairing system and the points cannot change.
/// Title, time control and boards can still change between rounds.</param>
/// <param name="Location">Where the tournament is played; optional, used for the report.</param>
/// <param name="Description">Free description of the tournament; optional, used for the report.</param>
public record TournamentDto(
    int Id,
    string Title,
    string? Location,
    string? Description,
    TournamentState State,
    TournamentSettingsDto Settings,
    int RoundsStarted,
    bool SettingsLocked,
    TournamentReportDto Report);

/// <summary>The report of the tournament, written by an AI when the tournament is closed.</summary>
/// <param name="Text">Plain text with paragraphs separated by empty lines; null until ready.</param>
/// <param name="Error">Technical error message when writing failed.</param>
/// <param name="Model">The AI that wrote the report, e.g. "Claude (claude-opus-5-5)".</param>
/// <param name="HasPrompt">Whether the prompt that produced the report can be fetched (api/tournaments/{id}/report/prompt).</param>
/// <param name="Events">The noteworthy events given for the last requested report (pre-fills the wizard next time).</param>
/// <param name="WritingStyleId">The writing style chosen for the last requested report.</param>
/// <param name="WritingStyleName">Name of that writing style, as it was when the report was requested.</param>
public record TournamentReportDto(
    ReportStatus Status, string? Text, DateTime? GeneratedAt, string? Error, string? Model, bool HasPrompt,
    IReadOnlyList<string>? Events = null, int? WritingStyleId = null, string? WritingStyleName = null);

/// <summary>Asks the AI for a report of a closed tournament.</summary>
/// <param name="Events">Noteworthy events that are not in the results (e.g. "Teun knocked over his tea in round 4").
/// Empty lines are ignored.</param>
/// <param name="WritingStyleId">The writing style (see api/settings/writing-styles); null for no specific style.</param>
public record GenerateReportRequest(IReadOnlyList<string>? Events = null, int? WritingStyleId = null);

/// <summary>The prompt that was sent to the AI to write the report: instructions (system) and the tournament data (user).</summary>
public record ReportPromptDto(string System, string User, string? Model);

/// <param name="IsRated">Whether the games change the players' ratings. Locked once round 1 has started.</param>
public record TournamentSettingsDto(
    PairingSystem PairingSystem,
    int GameTimeMinutes,
    int IncrementSeconds,
    double PointsForWin,
    double PointsForDraw,
    double PointsForLoss,
    double PointsForBye,
    int Boards,
    bool IsRated = true)
{
    /// <summary>The settings a new tournament starts with.</summary>
    public static TournamentSettingsDto Default { get; } = new(
        PairingSystem.RoundRobin, GameTimeMinutes: 15, IncrementSeconds: 10,
        PointsForWin: 1, PointsForDraw: 0.5, PointsForLoss: 0, PointsForBye: 1, Boards: 8, IsRated: true);
}

public record CreateTournamentRequest(
    string Title, TournamentSettingsDto Settings, string? Location = null, string? Description = null);

public record UpdateTournamentRequest(
    string Title, TournamentSettingsDto Settings, string? Location = null, string? Description = null);

/// <summary>A player in a tournament. PlayerId refers to the central player list.</summary>
/// <param name="IsDeleted">The player has been deleted from the player list (but stays in the tournament).</param>
public record TournamentPlayerDto(int PlayerId, string Name, int Rating, bool IsActive, bool IsDeleted = false);

public record AddTournamentPlayerRequest(int PlayerId);

/// <param name="IsStarted">False for the next round that is paired but not started yet.</param>
/// <param name="StartedAt">When the round was started (UTC); null for the pending round.</param>
public record RoundDto(int Number, bool IsStarted, IReadOnlyList<GameDto> Games, DateTime? StartedAt = null);

/// <param name="Number">Order of the game in the pairing; queued games get a board in this order.</param>
/// <param name="Board">The board the game is (or was) played on; null while it waits in the queue, and for a bye.</param>
/// <param name="Black">Null when White has a bye.</param>
public record GameDto(int Id, int Number, int? Board, GamePlayerDto White, GamePlayerDto? Black, GameResult Result);

/// <param name="IsDeleted">The player has been deleted from the player list.</param>
public record GamePlayerDto(int PlayerId, string Name, bool IsDeleted = false);

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
    int Losses,
    bool IsDeleted = false);

public record ApiError(ApiErrorCode Code);

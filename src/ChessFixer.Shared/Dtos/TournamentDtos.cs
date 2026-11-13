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

/// <param name="OpenGames">Competition: the number of games still to play between the players in the competition
/// (they become "not played" when it is closed). Always 0 for Swiss.</param>
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
    TournamentReportDto Report,
    int OpenGames = 0);

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
/// <param name="PointsForAbsence">Points for a round a player was absent (not marked as present).</param>
/// <param name="PointsForBye">Swiss only: points for a bye.</param>
/// <param name="TimesAgainstEachOther">Competition only: how many games every pair plays (1 or 2).</param>
public record TournamentSettingsDto(
    PairingSystem PairingSystem,
    int GameTimeMinutes,
    int IncrementSeconds,
    double PointsForWin,
    double PointsForDraw,
    double PointsForLoss,
    double PointsForBye,
    int Boards,
    bool IsRated = true,
    double PointsForAbsence = 0,
    int TimesAgainstEachOther = 2)
{
    /// <summary>The settings a new tournament starts with.</summary>
    public static TournamentSettingsDto Default { get; } = new(
        PairingSystem.Swiss, GameTimeMinutes: 15, IncrementSeconds: 10,
        PointsForWin: 1, PointsForDraw: 0.5, PointsForLoss: 0, PointsForBye: 1, Boards: 8, IsRated: true, PointsForAbsence: 0,
        TimesAgainstEachOther: 2);
}

public record CreateTournamentRequest(
    string Title, TournamentSettingsDto Settings, string? Location = null, string? Description = null);

public record UpdateTournamentRequest(
    string Title, TournamentSettingsDto Settings, string? Location = null, string? Description = null);

/// <summary>A player in a tournament. PlayerId refers to the central player list.</summary>
/// <param name="IsDeleted">The player has been deleted from the player list (but stays in the tournament).</param>
/// <param name="IsPresent">Whether the player is there for the next round; an absent player is not paired.</param>
public record TournamentPlayerDto(int PlayerId, string Name, int Rating, bool IsActive, bool IsDeleted = false, bool IsPresent = true);

public record AddTournamentPlayerRequest(int PlayerId);

public record SetPresenceRequest(bool IsPresent);

/// <param name="SkipRepeatMeetings">Games in which the players would meet for the third time (or more) are not
/// played: both players are absent for this round only.</param>
public record StartRoundRequest(bool SkipRepeatMeetings = false);

/// <param name="IsStarted">False for the next round that is paired but not started yet.</param>
/// <param name="StartedAt">When the round was started (UTC); null for the pending round.</param>
/// <param name="Report">The AI-written report of this round (status None when none was requested).</param>
public record RoundDto(int Number, bool IsStarted, IReadOnlyList<GameDto> Games, DateTime? StartedAt = null,
    TournamentReportDto? Report = null)
{
    /// <summary>Started and every game has a result (byes, absences and catch-up games always have one).</summary>
    public bool IsFinished => IsStarted && Games.All(g => g.Result != GameResult.NotPlayed);
}

/// <param name="Number">Order of the game in the pairing; queued games get a board in this order.</param>
/// <param name="Board">The board the game is (or was) played on; null while it waits in the queue, and for a bye.</param>
/// <param name="Black">Null when White has a bye, is absent (<see cref="GameResult.Absent"/>) or has no opponent
/// (<see cref="GameResult.NoOpponent"/>).</param>
/// <param name="IsCatchUp">Competition: a game played outside a club evening, entered afterwards. It belongs to the
/// round that was the latest when it was entered, but has no board.</param>
/// <param name="PlayedAt">When a catch-up game was entered (UTC).</param>
/// <param name="TimesMetBefore">How often the two players met in earlier rounds of this tournament (forfeits not
/// counted, as in the pairing). 2 or more: this game is (at least) their third meeting.</param>
public record GameDto(int Id, int Number, int? Board, GamePlayerDto White, GamePlayerDto? Black, GameResult Result,
    int TimesMetBefore = 0, bool IsCatchUp = false, DateTime? PlayedAt = null);

/// <param name="IsDeleted">The player has been deleted from the player list.</param>
/// <param name="IsGuest">A guest player; only filled where the UI shows it (the player statistics).</param>
/// <param name="ClubName">The club of a guest player, if any; only filled together with <paramref name="IsGuest"/>.</param>
public record GamePlayerDto(int PlayerId, string Name, bool IsDeleted = false, bool IsGuest = false, string? ClubName = null);

public record SetResultRequest(GameResult Result);

/// <param name="Buchholz">Swiss tie-break; not used in a competition.</param>
/// <param name="Remaining">Competition: games still to play (against players still in the competition).</param>
/// <param name="Percentage">Competition: points as a percentage of the maximum for the games played; null before any game.</param>
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
    bool IsDeleted = false,
    int? Remaining = null,
    double? Percentage = null);

/// <summary>
/// Competition: every player against every other player, with the games played (results) and still to play.
/// Player ids are ids of the central player list.
/// </summary>
public record CompetitionScheduleDto(
    int TimesAgainstEachOther,
    IReadOnlyList<SchedulePlayerDto> Players,
    IReadOnlyList<ScheduleGameDto> Games,
    IReadOnlyList<OpenGameDto> OpenGames);

/// <param name="IsActive">False when the player withdrew: their open games are not paired.</param>
public record SchedulePlayerDto(int PlayerId, string Name, int Rating, bool IsActive, bool IsDeleted = false);

/// <summary>A game between two players. <paramref name="Result"/> is NotPlayed while it is being played.</summary>
public record ScheduleGameDto(int GameId, int Round, int WhitePlayerId, int BlackPlayerId, GameResult Result,
    bool IsCatchUp = false);

/// <summary>A game that still has to be played.</summary>
/// <param name="WhitePlayerId">Who has White in it; null when that is still open.</param>
public record OpenGameDto(int PlayerA, int PlayerB, int? WhitePlayerId);

/// <summary>Enters a game of a competition that was played outside a club evening.</summary>
public record CatchUpGameRequest(int WhitePlayerId, int BlackPlayerId, GameResult Result);

public record ApiError(ApiErrorCode Code);

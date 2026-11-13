using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>Error codes returned by the API. The client shows a (translated) text per code.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApiErrorCode>))]
public enum ApiErrorCode
{
    // 404
    TournamentNotFound,
    PlayerNotFound,
    GameNotFound,
    UserNotFound,
    WritingStyleNotFound,
    ClubNotFound,

    // 400
    TitleRequired,
    FirstNameRequired,
    InvalidSettings,
    InvalidResult,
    InvalidPhoto,
    InvalidRating,
    UserNameRequired,
    DisplayNameRequired,
    PasswordTooShort,
    CurrentPasswordIncorrect,
    WritingStyleNameRequired,
    ClubNameRequired,

    // 401
    InvalidCredentials,

    // 429 (the server sends no body; the client uses this code itself)
    TooManyLoginAttempts,

    // 409
    TournamentClosed,
    RoundInProgress,
    NoRoundInProgress,
    SettingsLocked,
    PlayerAlreadyInTournament,
    PlayerNotInTournament,
    PlayerInActiveGame,
    ResultNotEditable,
    NotEnoughPlayers,
    TooManyPlayers,
    NoValidPairing,
    /// <summary>Competition: every pair has played all its games.</summary>
    CompetitionComplete,
    /// <summary>Competition: none of the players who take part still have a game to play against each other.</summary>
    NoGamesBetweenPresentPlayers,
    /// <summary>Only possible in a competition (e.g. catch-up games).</summary>
    NotACompetition,
    /// <summary>Catch-up game: the two players have no open game with these colors.</summary>
    GameNotOpen,
    /// <summary>Catch-up game: the pair has this game in the current round.</summary>
    GameInCurrentRound,
    /// <summary>Cancelling round 1 while catch-up games belong to it.</summary>
    CatchUpGamesInRound,
    AiNotConfigured,
    ReportNotAvailable,
    UserNameTaken,
    /// <summary>The action cannot be reverted (not supported, failed, or nothing to revert).</summary>
    NotRevertible,
    AlreadyReverted,
    /// <summary>What the action changed has been changed again since; reverting would undo that as well.</summary>
    ChangedSinceAction,
    /// <summary>PGN: the text contains no moves, or is too long.</summary>
    InvalidPgn,
    /// <summary>PGN: only a game that was played over the board (won, lost or drawn) can have moves.</summary>
    PgnNotAllowed
}

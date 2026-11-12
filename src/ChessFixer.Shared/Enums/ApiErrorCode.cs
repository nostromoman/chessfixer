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
    AiNotConfigured,
    ReportNotAvailable,
    UserNameTaken,
    /// <summary>The action cannot be reverted (not supported, failed, or nothing to revert).</summary>
    NotRevertible,
    AlreadyReverted,
    /// <summary>What the action changed has been changed again since; reverting would undo that as well.</summary>
    ChangedSinceAction
}

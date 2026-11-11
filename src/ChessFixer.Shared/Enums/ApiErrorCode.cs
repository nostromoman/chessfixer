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
    UserNameTaken
}

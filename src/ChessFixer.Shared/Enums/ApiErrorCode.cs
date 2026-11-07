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

    // 400
    TitleRequired,
    FirstNameRequired,
    InvalidSettings,
    InvalidResult,
    InvalidPhoto,

    // 409
    TournamentClosed,
    RoundInProgress,
    SettingsLocked,
    PlayerAlreadyInTournament,
    PlayerNotInTournament,
    PlayerInActiveGame,
    PlayerInTournament,
    RoundRobinPlayersLocked,
    ResultNotEditable,
    NotEnoughPlayers,
    TooManyPlayers,
    NoValidPairing,
    AllRoundsPlayed
}

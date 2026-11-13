using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<GameResult>))]
public enum GameResult
{
    NotPlayed,
    WhiteWins,
    Draw,
    BlackWins,
    WhiteWinsByForfeit,
    BlackWinsByForfeit,
    Bye,

    /// <summary>The player was absent this round: no game and no opponent (Black is null), points for absence.</summary>
    Absent,

    /// <summary>Competition: the player took part this round but had no opponent left to play (odd number, or all
    /// remaining opponents absent or already paired). No game, no opponent (Black is null), no points.</summary>
    NoOpponent
}

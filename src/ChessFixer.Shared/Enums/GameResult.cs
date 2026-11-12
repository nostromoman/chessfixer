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
    Absent
}

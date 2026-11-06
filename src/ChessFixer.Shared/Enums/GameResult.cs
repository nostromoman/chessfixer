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
    Bye
}

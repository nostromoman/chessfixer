using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<TournamentState>))]
public enum TournamentState
{
    New,
    RoundActive,
    WaitingForNextRound,
    Closed
}

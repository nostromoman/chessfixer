using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<PairingSystem>))]
public enum PairingSystem
{
    Swiss,
    /// <summary>Round robin: everyone plays everyone; each round pairs the players who met least often.
    /// Players can join and leave between rounds.</summary>
    RoundRobin
}

using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<PairingSystem>))]
public enum PairingSystem
{
    Swiss,
    /// <summary>Round robin: a fixed group in which everyone plays everyone. The player list is fixed once round 1
    /// has started and nobody can be absent; a player who withdraws loses their remaining games by forfeit.</summary>
    RoundRobin
}

using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<PairingSystem>))]
public enum PairingSystem
{
    Swiss,
    RoundRobin
}

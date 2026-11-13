using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<PairingSystem>))]
public enum PairingSystem
{
    /// <summary>Players with similar scores play each other. For one evening, or too few rounds for everyone to play everyone.</summary>
    Swiss,

    /// <summary>Everyone plays everyone, once or twice. Each round only the players who take part are paired, using the
    /// games they still have to play against each other; missed games can be caught up (catch-up games).</summary>
    Competition
}

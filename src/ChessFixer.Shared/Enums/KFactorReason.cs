using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>Which rule gave a player's K-factor.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KFactorReason>))]
public enum KFactorReason
{
    /// <summary>The normal K.</summary>
    Default,
    /// <summary>A junior rated under 2300.</summary>
    Junior,
    /// <summary>Rated 2400 or more.</summary>
    Master,
    /// <summary>Was rated 2400 or more before; K stays 10.</summary>
    WasMaster
}

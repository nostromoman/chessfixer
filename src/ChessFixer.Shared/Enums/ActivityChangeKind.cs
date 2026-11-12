using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>What an action did with a row in the database (activity log).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActivityChangeKind>))]
public enum ActivityChangeKind
{
    Added,
    Modified,
    Deleted
}

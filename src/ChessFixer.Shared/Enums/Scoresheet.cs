using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>How sure the reader (Claude) is that a move on a handwritten scoresheet was read correctly.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MoveConfidence>))]
public enum MoveConfidence
{
    /// <summary>Clearly written: no doubt.</summary>
    High,
    /// <summary>Probably right, but another reading is possible.</summary>
    Medium,
    /// <summary>Hard to read, a guess, or not what was written (repaired or added).</summary>
    Low
}

/// <summary>Reading a scoresheet photo with Claude.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScoresheetReadStatus>))]
public enum ScoresheetReadStatus
{
    /// <summary>Not read (kept only, or reading not started).</summary>
    None,
    Reading,
    Done,
    Failed
}

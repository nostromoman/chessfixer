using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>How good a move was according to the engine analysis.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MoveQuality>))]
public enum MoveQuality
{
    /// <summary>The engine's best move.</summary>
    Best,
    /// <summary>Not the best move, but hardly worse.</summary>
    Good,
    /// <summary>?! — the chance to win dropped by 5 percentage points or more.</summary>
    Inaccuracy,
    /// <summary>? — 10 percentage points or more.</summary>
    Mistake,
    /// <summary>?? — 15 percentage points or more.</summary>
    Blunder
}

/// <summary>The state of the engine analysis of a game.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnalysisStatus>))]
public enum AnalysisStatus
{
    /// <summary>No analysis (yet), or it belongs to an older version of the moves.</summary>
    None,
    /// <summary>Waiting for the engine.</summary>
    Pending,
    Running,
    Done,
    Failed
}

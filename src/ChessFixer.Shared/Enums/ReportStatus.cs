using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>State of the AI-written report of a closed tournament.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportStatus>))]
public enum ReportStatus
{
    /// <summary>No report (yet), e.g. because no AI provider is configured.</summary>
    None,
    /// <summary>Waiting for or being written by the AI.</summary>
    Generating,
    Ready,
    Failed
}

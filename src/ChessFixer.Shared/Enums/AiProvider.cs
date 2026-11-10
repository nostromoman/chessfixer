using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>The AI service that writes the tournament report. More providers (e.g. Claude) can be added later.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AiProvider>))]
public enum AiProvider
{
    /// <summary>No AI: no report is written.</summary>
    None,
    Ollama,
    /// <summary>Claude, through the Anthropic API.</summary>
    Claude
}

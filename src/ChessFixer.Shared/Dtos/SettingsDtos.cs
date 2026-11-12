using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <summary>Application settings for the AI that writes tournament reports.
/// Each provider has its own block, so more providers can be added next to Ollama.</summary>
public record AiSettingsDto(AiProvider Provider, OllamaSettingsDto Ollama, ClaudeSettingsDto Claude, ReportPromptSettingsDto Report);

/// <summary>The configurable parts of the prompt for the report, with the built-in defaults (to restore them).</summary>
/// <param name="SystemPrompt">The instructions (rules) for the AI. The chosen writing style is added below it.</param>
/// <param name="DefaultWritingStyleId">The writing style preselected in the report wizard; null preselects the first.</param>
/// <param name="UpsetRatingDifference">A win against a player rated at least this much higher is given to the AI as a
/// notable result (a draw at twice the difference); 0 gives none.</param>
public record ReportPromptSettingsDto(
    string SystemPrompt, int? DefaultWritingStyleId, int UpsetRatingDifference,
    string DefaultSystemPrompt, int DefaultUpsetRatingDifference);

/// <summary>An empty system prompt restores the built-in default.</summary>
public record UpdateReportPromptSettings(string SystemPrompt, int? DefaultWritingStyleId, int UpsetRatingDifference);

/// <param name="Url">Base URL of the Ollama server, e.g. http://localhost:11434.</param>
public record OllamaSettingsDto(string Url, string Model);

/// <param name="Claude">Null keeps the current Claude settings.</param>
/// <param name="Report">Null keeps the current prompt settings.</param>
public record UpdateAiSettingsRequest(
    AiProvider Provider, UpdateOllamaSettings Ollama, UpdateClaudeSettings? Claude = null, UpdateReportPromptSettings? Report = null);

public record UpdateOllamaSettings(string Url, string Model);

/// <param name="Model">Claude model id, e.g. claude-opus-5-5.</param>
/// <param name="HasApiKey">The API key itself is never sent to the client.</param>
public record ClaudeSettingsDto(string Model, bool HasApiKey);

/// <param name="ApiKey">Null keeps the current API key; an empty string removes it.</param>
public record UpdateClaudeSettings(string Model, string? ApiKey);

/// <summary>Result of testing the connection with the AI provider.</summary>
/// <param name="Models">Models available on the server (when the provider can list them).</param>
/// <param name="Error">Technical error message when the test failed.</param>
public record AiConnectionTestResult(bool Success, IReadOnlyList<string> Models, string? Error);

/// <summary>A writing style for the AI report, chosen when the report is requested.</summary>
/// <param name="Description">Instructions for the AI, e.g. "Write for a primary school newsletter, for children aged 9 and up."</param>
/// <param name="IsDefault">Preselected in the report wizard (the standard writing style in the AI settings).</param>
public record WritingStyleDto(int Id, string Name, string Description, bool IsDefault = false);

public record SaveWritingStyleRequest(string Name, string Description);

/// <summary>The values a new tournament starts with (the tournament type has no default: it is always chosen).</summary>
public record TournamentDefaultsDto(
    int GameTimeMinutes,
    int IncrementSeconds,
    int Boards,
    double PointsForWin,
    double PointsForDraw,
    double PointsForLoss,
    double PointsForBye,
    double PointsForAbsence,
    bool IsRated)
{
    /// <summary>The settings of a new tournament of the given type.</summary>
    public TournamentSettingsDto ToSettings(PairingSystem pairingSystem) => new(
        pairingSystem, GameTimeMinutes, IncrementSeconds, PointsForWin, PointsForDraw, PointsForLoss,
        PointsForBye, Boards, IsRated, PointsForAbsence);
}

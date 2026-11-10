using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <summary>Application settings for the AI that writes tournament reports.
/// Each provider has its own block, so more providers can be added next to Ollama.</summary>
public record AiSettingsDto(AiProvider Provider, OllamaSettingsDto Ollama, ClaudeSettingsDto Claude);

/// <param name="Url">Base URL of the Ollama server, e.g. http://localhost:11434.</param>
public record OllamaSettingsDto(string Url, string Model);

/// <param name="Claude">Null keeps the current Claude settings.</param>
public record UpdateAiSettingsRequest(AiProvider Provider, UpdateOllamaSettings Ollama, UpdateClaudeSettings? Claude = null);

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

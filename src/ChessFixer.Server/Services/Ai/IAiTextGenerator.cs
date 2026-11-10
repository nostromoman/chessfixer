using ChessFixer.Server.Data;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Services.Ai;

/// <summary>A prompt for a text-generating AI: instructions (system) and the actual question with the data (user).</summary>
public record AiPrompt(string System, string User);

/// <summary>
/// One AI provider (Ollama, later e.g. Claude). Register an implementation per provider;
/// <see cref="AiSettingsService"/> picks the one that matches the configured <see cref="AiProvider"/>.
/// </summary>
public interface IAiTextGenerator
{
    AiProvider Provider { get; }

    /// <summary>Whether the settings contain everything this provider needs.</summary>
    bool IsConfigured(AppSettings settings);

    /// <summary>Provider and model for display, e.g. "Ollama (llama3.1)".</summary>
    string Describe(AppSettings settings);

    Task<string> GenerateAsync(AppSettings settings, AiPrompt prompt, CancellationToken cancellationToken);

    /// <summary>Checks the connection; returns the available models (may be empty when the provider cannot list them).</summary>
    Task<IReadOnlyList<string>> TestConnectionAsync(AppSettings settings, CancellationToken cancellationToken);
}

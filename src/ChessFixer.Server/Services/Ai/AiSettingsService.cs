using System.Text.Json;
using ChessFixer.Server.Data;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Ai;

/// <summary>Reads and changes the AI settings, and picks the AI provider that writes the reports.</summary>
public class AiSettingsService(ChessFixerDbContext db, IEnumerable<IAiTextGenerator> generators)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    public async Task<AiSettingsDto> GetAsync() => ToDto(await LoadAsync());

    public async Task<AiSettingsDto> UpdateAsync(UpdateAiSettingsRequest request)
    {
        var settings = await LoadAsync();
        Apply(settings, request);
        await db.SaveChangesAsync();
        return ToDto(settings);
    }

    /// <summary>Tests the given settings without saving them.</summary>
    public async Task<AiConnectionTestResult> TestAsync(UpdateAiSettingsRequest request)
    {
        var settings = await LoadAsync();
        Apply(settings, request);
        db.ChangeTracker.Clear(); // only test, never save these values

        var generator = generators.SingleOrDefault(g => g.Provider == settings.AiProvider);
        if (generator is null) return new AiConnectionTestResult(false, [], "Er is geen AI-aanbieder gekozen.");

        try
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            var models = await generator.TestConnectionAsync(settings, timeout.Token);
            return new AiConnectionTestResult(true, models, null);
        }
        catch (TaskCanceledException)
        {
            return new AiConnectionTestResult(false, [], $"Geen antwoord binnen {TestTimeout.TotalSeconds:0} seconden.");
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or JsonException)
        {
            return new AiConnectionTestResult(false, [], e.Message);
        }
    }

    /// <summary>The settings and the generator to write a report with, or null when no AI is configured.</summary>
    public async Task<(AppSettings Settings, IAiTextGenerator Generator)?> GetGeneratorAsync()
    {
        var settings = await LoadAsync();
        var generator = generators.SingleOrDefault(g => g.Provider == settings.AiProvider);
        return generator is not null && generator.IsConfigured(settings) ? (settings, generator) : null;
    }

    public async Task<bool> IsConfiguredAsync() => await GetGeneratorAsync() is not null;

    private async Task<AppSettings> LoadAsync()
    {
        var settings = await db.AppSettings.SingleOrDefaultAsync(s => s.Id == AppSettings.SingletonId);
        if (settings is not null) return settings;

        settings = new AppSettings();
        db.AppSettings.Add(settings);
        return settings;
    }

    private static void Apply(AppSettings settings, UpdateAiSettingsRequest request)
    {
        settings.AiProvider = Enum.IsDefined(request.Provider) ? request.Provider : AiProvider.None;

        var ollama = request.Ollama;
        settings.OllamaUrl = ollama?.Url?.Trim() ?? "";
        settings.OllamaModel = ollama?.Model?.Trim() ?? "";

        if (request.Claude is { } claude)
        {
            settings.ClaudeModel = claude.Model?.Trim() ?? "";
            if (claude.ApiKey is not null) settings.ClaudeApiKey = claude.ApiKey.Trim();
        }
    }

    private static AiSettingsDto ToDto(AppSettings s) => new(
        s.AiProvider,
        new OllamaSettingsDto(s.OllamaUrl, s.OllamaModel),
        new ClaudeSettingsDto(s.ClaudeModel, s.ClaudeApiKey.Length > 0));
}

using System.Text.Json;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Services.Reports;
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
        if (request.Report?.DefaultWritingStyleId is { } styleId && !await db.WritingStyles.AnyAsync(s => s.Id == styleId))
            throw new ApiException(ApiErrorCode.WritingStyleNotFound);

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

        if (request.Report is { } report)
        {
            // The default (or empty) is stored as null: "use the built-in default".
            settings.ReportSystemPrompt = CustomText(report.SystemPrompt, TournamentReportPrompt.DefaultSystemPrompt,
                TournamentReportPrompt.MaxSystemPromptLength);
            settings.DefaultWritingStyleId = report.DefaultWritingStyleId;
            var upset = Math.Clamp(report.UpsetRatingDifference, 0, TournamentReportPrompt.MaxUpsetRatingDifference);
            settings.ReportUpsetRatingDifference = upset == TournamentReportPrompt.DefaultUpsetRatingDifference ? null : upset;
        }
    }

    private static string? CustomText(string? text, string defaultText, int maxLength)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0 || Normalize(trimmed) == Normalize(defaultText)) return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];

        static string Normalize(string s) => s.ReplaceLineEndings("\n").Trim();
    }

    /// <summary>The prompt settings, with the built-in defaults for what was not changed.</summary>
    public static ReportPromptOptions PromptOptionsOf(AppSettings s) => new(
        s.ReportSystemPrompt ?? TournamentReportPrompt.DefaultSystemPrompt,
        s.ReportUpsetRatingDifference ?? TournamentReportPrompt.DefaultUpsetRatingDifference);

    /// <summary>The writing style preselected in the report wizard; null when none is set.</summary>
    public async Task<int?> GetDefaultWritingStyleIdAsync() =>
        await db.AppSettings.Where(s => s.Id == AppSettings.SingletonId).Select(s => s.DefaultWritingStyleId).SingleOrDefaultAsync();

    private static AiSettingsDto ToDto(AppSettings s)
    {
        var prompt = PromptOptionsOf(s);
        return new AiSettingsDto(
            s.AiProvider,
            new OllamaSettingsDto(s.OllamaUrl, s.OllamaModel),
            new ClaudeSettingsDto(s.ClaudeModel, s.ClaudeApiKey.Length > 0),
            new ReportPromptSettingsDto(
                prompt.SystemPrompt, s.DefaultWritingStyleId, prompt.UpsetRatingDifference,
                TournamentReportPrompt.DefaultSystemPrompt, TournamentReportPrompt.DefaultUpsetRatingDifference));
    }
}

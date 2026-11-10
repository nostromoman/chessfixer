using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using ChessFixer.Server.Data;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessFixer.Server.Tests;

/// <summary>Location and description, the AI settings and the AI-written report after closing.</summary>
public class TournamentReportTests : ApiTestBase
{
    private readonly FakeAi _ai = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IAiTextGenerator>();
        services.AddSingleton<IAiTextGenerator>(_ai);
    }

    [Fact]
    public async Task LocationAndDescription_AreStoredAndCanChange()
    {
        var created = await PostAsync<TournamentDto>("api/tournaments",
            new CreateTournamentRequest("Zomerrapid", TournamentSettingsDto.Default, "  Clubhuis De Toren ", ""));
        Assert.Equal("Clubhuis De Toren", created.Location);
        Assert.Null(created.Description);

        // Can still change after round 1, like the title.
        await AddNewPlayersAsync(created.Id, 2);
        await StartRoundAsync(created.Id);
        await FinishActiveRoundAsync(created.Id);

        var updated = await PutAsync<TournamentDto>($"api/tournaments/{created.Id}",
            new UpdateTournamentRequest("Zomerrapid", created.Settings, "Dorpshuis", "Met taart voor de winnaar"));
        Assert.Equal("Dorpshuis", updated.Location);
        Assert.Equal("Met taart voor de winnaar", updated.Description);
    }

    [Fact]
    public async Task AiSettings_AreSavedAndCanBeTestedWithoutSaving()
    {
        var settings = await GetAsync<AiSettingsDto>("api/settings/ai");
        Assert.Equal(AiProvider.Ollama, settings.Provider);
        Assert.Equal("", settings.Ollama.Url);

        settings = await PutAsync<AiSettingsDto>("api/settings/ai", OllamaSettings(model: " ander-model "));
        Assert.Equal("http://localhost:11434", settings.Ollama.Url);
        Assert.Equal("ander-model", settings.Ollama.Model);

        // Testing uses the given settings, but does not save them.
        var result = await PostAsync<AiConnectionTestResult>("api/settings/ai/test", OllamaSettings(model: "getest-model"));
        Assert.True(result.Success);
        Assert.Equal("getest-model", _ai.LastSettings!.OllamaModel);
        Assert.Equal("ander-model", (await GetAsync<AiSettingsDto>("api/settings/ai")).Ollama.Model);
    }

    [Fact]
    public async Task Close_WithoutAi_WritesNoReport()
    {
        await PutAsync<AiSettingsDto>("api/settings/ai", OllamaSettings() with { Provider = AiProvider.None });
        var id = await PlayAndCloseAsync();

        var tournament = await GetTournamentAsync(id);
        Assert.Equal(ReportStatus.None, tournament.Report.Status);
        await AssertErrorAsync(await Client.PostAsync($"api/tournaments/{id}/report", null),
            HttpStatusCode.Conflict, ApiErrorCode.AiNotConfigured);
    }

    [Fact]
    public async Task Close_WithAi_WritesReportInTheBackground()
    {
        await PutAsync<AiSettingsDto>("api/settings/ai", OllamaSettings());
        var created = await PostAsync<TournamentDto>("api/tournaments",
            new CreateTournamentRequest("Zomerrapid", TournamentSettingsDto.Default, "Clubhuis De Toren", "Jaarlijks toernooi"));
        var id = created.Id;
        await AddNewPlayersAsync(id, 3);
        await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);

        var closed = await PostAsync<TournamentDto>($"api/tournaments/{id}/close", null);
        Assert.Equal(ReportStatus.Generating, closed.Report.Status);

        var report = await WaitForReportAsync(id);
        Assert.Equal(ReportStatus.Ready, report.Status);
        Assert.Equal("Titel\n\nVerslag 1", report.Text);
        Assert.NotNull(report.GeneratedAt);

        Assert.Equal("Fake (test-model)", report.Model);
        Assert.True(report.HasPrompt);

        var prompt = Assert.Single(_ai.Prompts);
        var stored = await GetAsync<ReportPromptDto>($"api/tournaments/{id}/report/prompt");
        Assert.Equal(prompt.System, stored.System);
        Assert.Equal(prompt.User, stored.User);
        Assert.Equal("Fake (test-model)", stored.Model);

        Assert.Contains("Zomerrapid", prompt.User);
        Assert.Contains("Clubhuis De Toren", prompt.User);
        Assert.Contains("Jaarlijks toernooi", prompt.User);
        Assert.Contains("Speler 1", prompt.User);
        Assert.Contains("EINDSTAND", prompt.User);
    }

    [Fact]
    public async Task FailedReport_CanBeWrittenAgain()
    {
        await PutAsync<AiSettingsDto>("api/settings/ai", OllamaSettings());
        _ai.Fail = true;
        var id = await PlayAndCloseAsync();

        var report = await WaitForReportAsync(id);
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal("AI is stuk", report.Error);

        _ai.Fail = false;
        await PostAsync<TournamentDto>($"api/tournaments/{id}/report", null);
        report = await WaitForReportAsync(id);
        Assert.Equal(ReportStatus.Ready, report.Status);
        Assert.Null(report.Error);
    }

    [Fact]
    public async Task ReportPrompt_NotAvailableWithoutReport()
    {
        var id = await CreateTournamentAsync();
        await AssertErrorAsync(await Client.GetAsync($"api/tournaments/{id}/report/prompt"),
            HttpStatusCode.Conflict, ApiErrorCode.ReportNotAvailable);
    }

    [Fact]
    public async Task ClaudeSettings_ApiKeyIsNeverReturnedAndKeptWhenNull()
    {
        var settings = await GetAsync<AiSettingsDto>("api/settings/ai");
        Assert.Equal("claude-opus-5-5", settings.Claude.Model);
        Assert.False(settings.Claude.HasApiKey);

        settings = await PutAsync<AiSettingsDto>("api/settings/ai",
            OllamaSettings() with { Provider = AiProvider.Claude, Claude = new UpdateClaudeSettings("claude-opus-5-5", "sk-ant-geheim") });
        Assert.Equal(AiProvider.Claude, settings.Provider);
        Assert.True(settings.Claude.HasApiKey);
        Assert.DoesNotContain("sk-ant-geheim", await Client.GetStringAsync("api/settings/ai"));

        // Null keeps the key; leaving out the Claude block keeps all Claude settings.
        settings = await PutAsync<AiSettingsDto>("api/settings/ai",
            OllamaSettings() with { Provider = AiProvider.Claude, Claude = new UpdateClaudeSettings("claude-sonnet-5-5", null) });
        Assert.True(settings.Claude.HasApiKey);
        Assert.Equal("claude-sonnet-5-5", settings.Claude.Model);
        settings = await PutAsync<AiSettingsDto>("api/settings/ai", OllamaSettings());
        Assert.True(settings.Claude.HasApiKey);
        Assert.Equal("claude-sonnet-5-5", settings.Claude.Model);
    }

    [Fact]
    public async Task Report_OnlyForClosedTournament()
    {
        await PutAsync<AiSettingsDto>("api/settings/ai", OllamaSettings());
        var id = await CreateTournamentAsync();
        await AssertErrorAsync(await Client.PostAsync($"api/tournaments/{id}/report", null),
            HttpStatusCode.Conflict, ApiErrorCode.ReportNotAvailable);
    }

    // ---------- Helpers ----------

    private static UpdateAiSettingsRequest OllamaSettings(string model = "test-model") =>
        new(AiProvider.Ollama, new UpdateOllamaSettings("http://localhost:11434", model));

    private async Task<int> PlayAndCloseAsync()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);
        await PostAsync<TournamentDto>($"api/tournaments/{id}/close", null);
        return id;
    }

    private async Task<TournamentReportDto> WaitForReportAsync(int id)
    {
        for (var i = 0; i < 100; i++)
        {
            var report = (await GetTournamentAsync(id)).Report;
            if (report.Status != ReportStatus.Generating) return report;
            await Task.Delay(50);
        }
        throw new TimeoutException("The report was not written in time.");
    }

    private sealed class FakeAi : IAiTextGenerator
    {
        public ConcurrentQueue<AiPrompt> Prompts { get; } = new();
        public AppSettings? LastSettings { get; private set; }
        public bool Fail { get; set; }

        public AiProvider Provider => AiProvider.Ollama;

        public bool IsConfigured(AppSettings settings) => settings.OllamaModel.Length > 0;

        public string Describe(AppSettings settings) => $"Fake ({settings.OllamaModel})";

        public Task<string> GenerateAsync(AppSettings settings, AiPrompt prompt, CancellationToken cancellationToken)
        {
            LastSettings = settings;
            if (Fail) throw new HttpRequestException("AI is stuk");
            Prompts.Enqueue(prompt);
            return Task.FromResult($"Titel\n\nVerslag {Prompts.Count}");
        }

        public Task<IReadOnlyList<string>> TestConnectionAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            LastSettings = settings;
            return Task.FromResult<IReadOnlyList<string>>(["test-model"]);
        }
    }
}

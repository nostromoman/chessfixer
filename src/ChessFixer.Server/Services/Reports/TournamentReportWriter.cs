using ChessFixer.Server.Data;
using ChessFixer.Server.Hubs;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Shared.Enums;
using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Reports;

/// <summary>Has the configured AI write the report of one tournament and stores it.</summary>
public class TournamentReportWriter(
    ChessFixerDbContext db,
    TournamentService tournaments,
    AiSettingsService aiSettings,
    IHubContext<TournamentHub> hub,
    TimeProvider time,
    ILogger<TournamentReportWriter> logger)
{
    public async Task WriteAsync(int tournamentId, CancellationToken cancellationToken)
    {
        var status = await db.TournamentReports
            .Where(r => r.TournamentId == tournamentId)
            .Select(r => (ReportStatus?)r.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (status != ReportStatus.Generating) return; // deleted, or already handled

        if (await aiSettings.GetGeneratorAsync() is not { } ai)
        {
            await StoreAsync(tournamentId, ReportStatus.Failed, null, "Er is geen AI ingesteld.", cancellationToken);
            return;
        }

        string text;
        AiPrompt prompt;
        var model = ai.Generator.Describe(ai.Settings);
        try
        {
            prompt = await BuildPromptAsync(tournamentId, AiSettingsService.PromptOptionsOf(ai.Settings));
            logger.LogInformation("Writing the report of tournament {TournamentId} with {Model}.", tournamentId, model);
            text = await ai.Generator.GenerateAsync(ai.Settings, prompt, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "The AI could not write the report of tournament {TournamentId}.", tournamentId);
            var message = e is TaskCanceledException ? "De AI gaf niet op tijd antwoord." : e.Message;
            await StoreAsync(tournamentId, ReportStatus.Failed, null, message, cancellationToken);
            return;
        }

        await db.TournamentReports
            .Where(r => r.TournamentId == tournamentId && r.Status == ReportStatus.Generating)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Model, model)
                .SetProperty(r => r.SystemPrompt, prompt.System)
                .SetProperty(r => r.UserPrompt, prompt.User), cancellationToken);
        await StoreAsync(tournamentId, ReportStatus.Ready, text, null, cancellationToken);
    }

    private async Task<AiPrompt> BuildPromptAsync(int tournamentId, ReportPromptOptions options)
    {
        var tournament = await tournaments.GetAsync(tournamentId);
        var rounds = await tournaments.GetRoundsAsync(tournamentId);
        var standings = await tournaments.GetStandingsAsync(tournamentId);

        var playerIds = standings.Select(s => s.PlayerId).ToList();
        var players = await db.Players.Where(p => playerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var startedAt = await db.Rounds
            .Where(r => r.TournamentId == tournamentId && r.StartedAt != null)
            .Select(r => r.StartedAt)
            .OrderBy(s => s)
            .ToListAsync();
        var wizard = await db.TournamentReports
            .Where(r => r.TournamentId == tournamentId)
            .Select(r => new { r.Events, r.WritingStyleDescription })
            .SingleAsync();

        return TournamentReportPrompt.Build(new TournamentReportData(
            tournament, rounds, standings, players, startedAt.FirstOrDefault(), startedAt.LastOrDefault(),
            wizard.Events, wizard.WritingStyleDescription), options);
    }

    /// <summary>
    /// Only touches the report, so it cannot conflict with other changes to the tournament.
    /// When writing failed (text null), an earlier report is kept.
    /// </summary>
    private async Task StoreAsync(int tournamentId, ReportStatus status, string? text, string? error, CancellationToken cancellationToken)
    {
        DateTime? generatedAt = text is null ? null : time.GetUtcNow().UtcDateTime;
        await db.TournamentReports
            .Where(r => r.TournamentId == tournamentId && r.Status == ReportStatus.Generating)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.Text, r => text ?? r.Text)
                .SetProperty(r => r.GeneratedAt, r => generatedAt ?? r.GeneratedAt)
                .SetProperty(r => r.Error, error), cancellationToken);

        await hub.Clients.All.SendAsync(TournamentHubContract.TournamentChanged, tournamentId, cancellationToken);
    }
}

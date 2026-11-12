using ChessFixer.Server.Data;
using ChessFixer.Server.Hubs;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Shared.Enums;
using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Reports;

/// <summary>Has the configured AI write the report of a tournament or of one round, and stores it.</summary>
public class TournamentReportWriter(
    ChessFixerDbContext db,
    TournamentService tournaments,
    AiSettingsService aiSettings,
    IHubContext<TournamentHub> hub,
    TimeProvider time,
    ILogger<TournamentReportWriter> logger)
{
    public Task WriteAsync(ReportJob job, CancellationToken cancellationToken) =>
        job.RoundId is { } roundId
            ? WriteAsync(db.RoundReports.Where(r => r.RoundId == roundId), job, cancellationToken)
            : WriteAsync(db.TournamentReports.Where(r => r.TournamentId == job.TournamentId), job, cancellationToken);

    /// <param name="report">The one report row to write (a query, so it can be updated without loading it).</param>
    private async Task WriteAsync<T>(IQueryable<T> report, ReportJob job, CancellationToken cancellationToken) where T : AiReport
    {
        var status = await report.Select(r => (ReportStatus?)r.Status).SingleOrDefaultAsync(cancellationToken);
        if (status != ReportStatus.Generating) return; // deleted, or already handled

        if (await aiSettings.GetGeneratorAsync() is not { } ai)
        {
            await StoreAsync(report, job, ReportStatus.Failed, null, "Er is geen AI ingesteld.", cancellationToken);
            return;
        }

        string text;
        AiPrompt prompt;
        var model = ai.Generator.Describe(ai.Settings);
        try
        {
            var wizard = await report.Select(r => new { r.Events, r.WritingStyleDescription }).SingleAsync(cancellationToken);
            var options = AiSettingsService.PromptOptionsOf(ai.Settings);
            prompt = job.RoundId is { } roundId
                ? await BuildRoundPromptAsync(job.TournamentId, roundId, wizard.Events, wizard.WritingStyleDescription,
                    options with { SystemPrompt = TournamentReportPrompt.DefaultRoundSystemPrompt })
                : await BuildPromptAsync(job.TournamentId, wizard.Events, wizard.WritingStyleDescription, options);
            logger.LogInformation("Writing the report of tournament {TournamentId} (round id {RoundId}) with {Model}.",
                job.TournamentId, job.RoundId, model);
            text = await ai.Generator.GenerateAsync(ai.Settings, prompt, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "The AI could not write the report of tournament {TournamentId} (round id {RoundId}).",
                job.TournamentId, job.RoundId);
            var message = e is TaskCanceledException ? "De AI gaf niet op tijd antwoord." : e.Message;
            await StoreAsync(report, job, ReportStatus.Failed, null, message, cancellationToken);
            return;
        }

        await report
            .Where(r => r.Status == ReportStatus.Generating)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Model, model)
                .SetProperty(r => r.SystemPrompt, prompt.System)
                .SetProperty(r => r.UserPrompt, prompt.User), cancellationToken);
        await StoreAsync(report, job, ReportStatus.Ready, text, null, cancellationToken);
    }

    private async Task<AiPrompt> BuildPromptAsync(
        int tournamentId, IReadOnlyList<string> events, string? writingStyle, ReportPromptOptions options)
    {
        var tournament = await tournaments.GetAsync(tournamentId);
        var rounds = await tournaments.GetRoundsAsync(tournamentId);
        var standings = await tournaments.GetStandingsAsync(tournamentId);
        var players = await PlayersAsync(tournamentId, standings.Select(s => s.PlayerId));
        var startedAt = await StartedAtAsync(tournamentId);

        return TournamentReportPrompt.Build(new TournamentReportData(
            tournament, rounds, standings, players, startedAt.FirstOrDefault(), startedAt.LastOrDefault(),
            events, writingStyle), options);
    }

    private async Task<AiPrompt> BuildRoundPromptAsync(
        int tournamentId, int roundId, IReadOnlyList<string> events, string? writingStyle, ReportPromptOptions options)
    {
        var number = await db.Rounds.Where(r => r.Id == roundId).Select(r => r.Number).SingleAsync();
        var tournament = await tournaments.GetAsync(tournamentId);
        var rounds = await tournaments.GetRoundsAsync(tournamentId);
        var standings = await tournaments.GetStandingsAsync(tournamentId, afterRound: number);
        var players = await PlayersAsync(tournamentId, standings.Select(s => s.PlayerId));
        var startedAt = await StartedAtAsync(tournamentId);

        return TournamentReportPrompt.BuildRound(new TournamentReportData(
            tournament, rounds, standings, players, startedAt.FirstOrDefault(), startedAt.LastOrDefault(),
            events, writingStyle), number, options);
    }

    /// <summary>
    /// Player details by player id. The ratings changed when the tournament was closed: the report uses the ratings
    /// the players had before it.
    /// </summary>
    private async Task<Dictionary<int, Player>> PlayersAsync(int tournamentId, IEnumerable<int> ids)
    {
        var playerIds = ids.ToList();
        var players = await db.Players.AsNoTracking().Include(p => p.Club).Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);
        var ratedGames = await db.Games
            .Where(g => g.Round.TournamentId == tournamentId && g.RatedAt != null)
            .Select(g => new { White = g.White.PlayerId, Black = g.Black!.PlayerId, g.WhiteRatingBefore, g.BlackRatingBefore })
            .ToListAsync();
        foreach (var game in ratedGames)
        {
            if (players.TryGetValue(game.White, out var white)) white.Rating = game.WhiteRatingBefore!.Value;
            if (players.TryGetValue(game.Black, out var black)) black.Rating = game.BlackRatingBefore!.Value;
        }
        return players;
    }

    private Task<List<DateTime?>> StartedAtAsync(int tournamentId) =>
        db.Rounds
            .Where(r => r.TournamentId == tournamentId && r.StartedAt != null)
            .Select(r => r.StartedAt)
            .OrderBy(s => s)
            .ToListAsync();

    /// <summary>
    /// Only touches the report, so it cannot conflict with other changes to the tournament.
    /// When writing failed (text null), an earlier report is kept.
    /// </summary>
    private async Task StoreAsync<T>(IQueryable<T> report, ReportJob job, ReportStatus status, string? text, string? error,
        CancellationToken cancellationToken) where T : AiReport
    {
        DateTime? generatedAt = text is null ? null : time.GetUtcNow().UtcDateTime;
        await report
            .Where(r => r.Status == ReportStatus.Generating)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.Text, r => text ?? r.Text)
                .SetProperty(r => r.GeneratedAt, r => generatedAt ?? r.GeneratedAt)
                .SetProperty(r => r.Error, error), cancellationToken);

        await hub.Clients.All.SendAsync(TournamentHubContract.TournamentChanged, job.TournamentId, cancellationToken);
    }
}

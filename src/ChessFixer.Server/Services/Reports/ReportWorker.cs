using ChessFixer.Server.Data;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Reports;

/// <summary>
/// Writes the reports in the background, so closing a tournament does not wait for the AI
/// (which can take minutes). Clients are notified through SignalR when a report is done.
/// </summary>
public class ReportWorker(ReportQueue queue, IServiceScopeFactory scopes, ILogger<ReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TournamentReportWriter>().WriteAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: the report stays "generating" and is picked up again at the next start.
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Writing the report of tournament {TournamentId} (round id {RoundId}) failed.", job.TournamentId, job.RoundId);
            }
        }
    }

    /// <summary>Reports that were still being written when the server stopped.</summary>
    private async Task RequeueUnfinishedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChessFixerDbContext>();
            var tournamentIds = await db.TournamentReports
                .Where(r => r.Status == ReportStatus.Generating)
                .Select(r => r.TournamentId)
                .ToListAsync(cancellationToken);
            tournamentIds.ForEach(id => queue.Enqueue(new ReportJob(id)));

            var rounds = await db.RoundReports
                .Where(r => r.Status == ReportStatus.Generating)
                .Select(r => new { r.Round.TournamentId, r.RoundId })
                .ToListAsync(cancellationToken);
            rounds.ForEach(r => queue.Enqueue(new ReportJob(r.TournamentId, r.RoundId)));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Could not look up unfinished reports.");
        }
    }
}

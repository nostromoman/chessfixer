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

        await foreach (var tournamentId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TournamentReportWriter>().WriteAsync(tournamentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: the report stays "generating" and is picked up again at the next start.
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Writing the report of tournament {TournamentId} failed.", tournamentId);
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
            var ids = await db.Tournaments
                .Where(t => t.ReportStatus == ReportStatus.Generating)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
            ids.ForEach(queue.Enqueue);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Could not look up unfinished reports.");
        }
    }
}

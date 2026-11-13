using System.ComponentModel;
using System.Text.Json;
using ChessFixer.Core.Chess;
using ChessFixer.Server.Data;
using ChessFixer.Server.Hubs;
using ChessFixer.Shared.Enums;
using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Analysis;

/// <summary>
/// Analyses games with Stockfish in the background, one at a time: every position of the game at a fixed depth.
/// Progress is saved every few positions, and clients are notified (GameAnalysisChanged) so the page can show it.
/// </summary>
public class AnalysisWorker(
    AnalysisQueue queue,
    IServiceScopeFactory scopes,
    IChessEngineFactory engines,
    IHubContext<TournamentHub> hub,
    ILogger<AnalysisWorker> logger) : BackgroundService
{
    private const int SaveEvery = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var analysisId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await AnalyseAsync(analysisId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // shutting down: the analysis stays Running and is started again at the next start
            }
            catch (Exception e)
            {
                logger.LogError(e, "Analysis {AnalysisId} failed.", analysisId);
            }
        }
    }

    private async Task AnalyseAsync(int analysisId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChessFixerDbContext>();
        var analysis = await db.GameAnalyses.SingleOrDefaultAsync(a => a.Id == analysisId, cancellationToken);
        if (analysis is null || analysis.Status is AnalysisStatus.Done or AnalysisStatus.Failed) return; // replaced or done

        var fens = PgnGame.Parse(analysis.Pgn).Fens;
        analysis.Status = AnalysisStatus.Running;
        analysis.PositionsDone = 0;
        analysis.PositionsTotal = fens.Count;
        if (!await SaveAsync(db, analysis, cancellationToken)) return;

        try
        {
            await using var engine = await engines.StartAsync(cancellationToken);
            analysis.Engine = engine.Name;

            var positions = new List<AnalysedPosition>();
            foreach (var fen in fens)
            {
                var result = await engine.AnalyseAsync(fen, analysis.Depth, cancellationToken);
                positions.Add(new AnalysedPosition(result.Evaluation.Centipawns, result.Evaluation.Mate, result.BestMove, result.Line));
                analysis.PositionsDone = positions.Count;
                if (positions.Count % SaveEvery == 0 && positions.Count < fens.Count
                    && !await SaveAsync(db, analysis, cancellationToken))
                    return; // the moves were changed meanwhile: a new analysis replaces this one
            }

            analysis.Positions = JsonSerializer.Serialize(positions, GameAnalysisService.Json);
            analysis.Status = AnalysisStatus.Done;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "Analysis {AnalysisId} of game {GameId} failed.", analysis.Id, analysis.GameId);
            analysis.Status = AnalysisStatus.Failed;
            analysis.Error = e is Win32Exception
                ? "Stockfish is niet gevonden op de server (instelling Stockfish:Path)."
                : e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
        }

        analysis.FinishedAt = DateTime.UtcNow;
        await SaveAsync(db, analysis, cancellationToken);
    }

    /// <summary>Saves the progress and notifies the clients; false when the analysis was removed meanwhile.</summary>
    private async Task<bool> SaveAsync(ChessFixerDbContext db, GameAnalysis analysis, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        await hub.Clients.All.SendAsync(TournamentHubContract.GameAnalysisChanged, analysis.GameId, cancellationToken);
        return true;
    }

    /// <summary>Analyses that were waiting or busy when the server stopped.</summary>
    private async Task RequeueUnfinishedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChessFixerDbContext>();
            var ids = await db.GameAnalyses
                .Where(a => a.Status == AnalysisStatus.Pending || a.Status == AnalysisStatus.Running)
                .OrderBy(a => a.Id)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);
            ids.ForEach(queue.Enqueue);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Could not look up unfinished analyses.");
        }
    }
}

using System.Text.Json;
using System.Threading.Channels;
using ChessFixer.Core.Chess;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Hubs;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessFixer.Server.Services.Analysis;

/// <summary>One analysed position, as stored (JSON) in <see cref="GameAnalysis.Positions"/>. Evaluation from White's
/// point of view; Best and Line in UCI notation.</summary>
public sealed record AnalysedPosition(int? Cp, int? Mate, string? Best, IReadOnlyList<string> Line)
{
    public Evaluation Evaluation => new(Cp, Mate);
}

/// <summary>Analyses (ids of <see cref="GameAnalysis"/>) waiting for the engine; processed one by one by <see cref="AnalysisWorker"/>.</summary>
public class AnalysisQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(int analysisId) => _channel.Writer.TryWrite(analysisId);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>The games with moves (PGN) and their Stockfish analysis.</summary>
public class GameAnalysisService(
    ChessFixerDbContext db,
    AnalysisQueue queue,
    IHubContext<TournamentHub> hub,
    IOptions<StockfishOptions> options)
{
    /// <summary>The best line is shown up to this many half-moves.</summary>
    private const int LineLength = 8;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IQueryable<Game> GamesWithMoves() =>
        db.Games
            .Where(g => g.Pgn != null && g.BlackId != null)
            .Include(g => g.Round).ThenInclude(r => r.Tournament)
            .Include(g => g.White).ThenInclude(p => p.Player)
            .Include(g => g.Black!).ThenInclude(p => p.Player)
            .Include(g => g.Analysis)
            .AsSplitQuery();

    /// <summary>All games with moves, newest first.</summary>
    public async Task<List<AnalysedGameSummaryDto>> GetAllAsync()
    {
        var games = await GamesWithMoves().ToListAsync();
        return games
            .Select(g =>
            {
                var report = Build(g);
                return new AnalysedGameSummaryDto(g.Id, g.Round.TournamentId, g.Round.Tournament.Title, g.Round.Number,
                    g.IsCatchUp, DateOf(g), Player(g.White), Player(g.Black!), g.Result,
                    (report.Plies.Count + 1) / 2, report.Analysis);
            })
            .OrderByDescending(g => g.Date)
            .ThenByDescending(g => g.GameId)
            .ToList();
    }

    public async Task<GameAnalysisDto> GetAsync(int gameId)
    {
        var game = await GamesWithMoves().SingleOrDefaultAsync(g => g.Id == gameId)
                   ?? throw new ApiException(ApiErrorCode.GameNotFound);
        return Build(game);
    }

    /// <summary>
    /// Starts a new analysis of the game's current moves (replacing an old one), or removes the analysis when the
    /// game has no moves any more. Called when the moves change, and to analyse again.
    /// </summary>
    public async Task ResetAsync(int gameId)
    {
        var game = await db.Games.Include(g => g.Analysis).SingleOrDefaultAsync(g => g.Id == gameId)
                   ?? throw new ApiException(ApiErrorCode.GameNotFound);

        if (game.Analysis is not null)
        {
            db.GameAnalyses.Remove(game.Analysis);
            await db.SaveChangesAsync(); // the unique GameId must be free before the new row is added
        }

        GameAnalysis? analysis = null;
        if (game.Pgn is not null)
        {
            analysis = new GameAnalysis
            {
                GameId = game.Id,
                Pgn = game.Pgn,
                Depth = Math.Clamp(options.Value.Depth, 1, 30),
                PositionsTotal = PgnGame.Parse(game.Pgn).Fens.Count,
                RequestedAt = DateTime.UtcNow
            };
            db.GameAnalyses.Add(analysis);
            await db.SaveChangesAsync();
            queue.Enqueue(analysis.Id);
        }

        await hub.Clients.All.SendAsync(TournamentHubContract.GameAnalysisChanged, gameId);
    }

    private static DateTime? DateOf(Game game) => game.IsCatchUp ? game.PlayedAt : game.Round.StartedAt;

    private static GamePlayerDto Player(TournamentPlayer player) =>
        new(player.PlayerId, player.Player.Name, player.Player.IsDeleted);

    /// <summary>The moves replayed, joined with the engine evaluations when the analysis of these moves is done.</summary>
    private static GameAnalysisDto Build(Game game)
    {
        var parsed = PgnGame.Parse(game.Pgn!);
        var analysis = game.Analysis is { } a && a.Pgn == game.Pgn ? a : null;
        var positions = analysis is { Status: AnalysisStatus.Done, Positions: { } json }
            ? JsonSerializer.Deserialize<List<AnalysedPosition>>(json, Json)
            : null;
        if (positions is not null && positions.Count != parsed.Plies.Count + 1) positions = null; // parser changed since

        var fens = parsed.Fens;
        var plies = new List<PlyDto>();
        var counts = new Dictionary<bool, int[]> { [true] = new int[3], [false] = new int[3] };
        var accuracies = new Dictionary<bool, List<double>> { [true] = [], [false] = [] };

        for (var i = 0; i < parsed.Plies.Count; i++)
        {
            var ply = parsed.Plies[i];
            var before = Position.FromFen(fens[i]);
            var white = before.WhiteToMove;
            var dto = new PlyDto(before.FullmoveNumber, white, ply.San, ply.Uci, ply.FenAfter);

            if (positions is not null)
            {
                var (b, after) = (positions[i], positions[i + 1]);
                var judgement = MoveJudgement.Judge(b.Evaluation, after.Evaluation, white, playedBest: b.Best == ply.Uci);
                accuracies[white].Add(judgement.Accuracy);
                if (judgement.Quality >= MoveQuality.Inaccuracy) counts[white][judgement.Quality - MoveQuality.Inaccuracy]++;

                var bestMove = b.Best is null ? null : before.ParseUci(b.Best);
                dto = dto with
                {
                    Evaluation = ToDto(after.Evaluation, !white),
                    Quality = judgement.Quality,
                    WinChanceLoss = Math.Round(judgement.WinChanceLoss, 1),
                    BestSan = bestMove is { } m ? before.ToSan(m) : null,
                    BestUci = bestMove?.ToUci(),
                    BestEvaluation = ToDto(b.Evaluation, white),
                    BestLine = Line(before, b.Line),
                    NextLine = Line(Position.FromFen(ply.FenAfter), after.Line)
                };
            }
            plies.Add(dto);
        }

        AnalysisSummaryDto summary;
        if (analysis is null)
            summary = new AnalysisSummaryDto(AnalysisStatus.None);
        else if (positions is null)
            summary = new AnalysisSummaryDto(analysis.Status == AnalysisStatus.Done ? AnalysisStatus.None : analysis.Status,
                analysis.PositionsDone, analysis.PositionsTotal);
        else
            summary = new AnalysisSummaryDto(AnalysisStatus.Done, analysis.PositionsDone, analysis.PositionsTotal,
                Average(accuracies[true]), Average(accuracies[false]), Count(counts[true]), Count(counts[false]));

        var start = positions?[0];
        return new GameAnalysisDto(
            game.Id, game.Round.TournamentId, game.Round.Tournament.Title, game.Round.Number, game.IsCatchUp, DateOf(game),
            Player(game.White), Player(game.Black!), game.Result, game.Pgn!, parsed.StartFen, plies,
            parsed.Error is { } e ? new PgnMoveErrorDto(e.MoveNumber, e.White, e.Text) : null,
            summary,
            start is null ? null : ToDto(start.Evaluation, Position.FromFen(parsed.StartFen).WhiteToMove),
            analysis?.Engine, analysis?.Depth, analysis?.FinishedAt,
            analysis?.Status == AnalysisStatus.Failed ? analysis.Error : null);

        static double? Average(List<double> values) => values.Count == 0 ? null : Math.Round(values.Average(), 1);
        static MoveQualityCountDto Count(int[] c) => new(c[0], c[1], c[2]);
    }

    private static EvaluationDto ToDto(Evaluation evaluation, bool whiteToMove) =>
        new(evaluation.Centipawns, evaluation.Mate, Math.Round(evaluation.WhiteWinChance(whiteToMove), 1));

    /// <summary>The engine line (UCI) as SAN, as far as it is legal (it always is, unless the data is damaged).</summary>
    private static List<string> Line(Position position, IReadOnlyList<string> uciLine)
    {
        var line = new List<string>();
        foreach (var uci in uciLine.Take(LineLength))
        {
            if (position.ParseUci(uci) is not { } move) break;
            line.Add(position.ToSan(move));
            position = position.Play(move);
        }
        return line;
    }
}

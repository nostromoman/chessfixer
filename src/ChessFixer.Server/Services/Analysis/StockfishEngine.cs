using System.Diagnostics;
using System.Globalization;
using ChessFixer.Core.Chess;
using Microsoft.Extensions.Options;

namespace ChessFixer.Server.Services.Analysis;

/// <summary>Settings (section "Stockfish"). In the Docker image Stockfish is installed as /usr/games/stockfish.</summary>
public class StockfishOptions
{
    /// <summary>The Stockfish executable: a full path, or a name found on the PATH.</summary>
    public string Path { get; set; } = "stockfish";

    /// <summary>Search depth in half-moves per position. 16 is far stronger than any club player and takes about a second per position.</summary>
    public int Depth { get; set; } = 16;

    /// <summary>CPU threads for the engine; 0 = half of the processors (at most 4), so the app stays responsive.</summary>
    public int Threads { get; set; }

    public int HashMb { get; set; } = 64;
}

/// <summary>The result of analysing one position.</summary>
/// <param name="BestMove">The best move in UCI notation; null when there is no move (checkmate, stalemate).</param>
/// <param name="Line">The best line in UCI notation, starting with the best move.</param>
public sealed record EngineResult(Evaluation Evaluation, string? BestMove, IReadOnlyList<string> Line);

public interface IChessEngine : IAsyncDisposable
{
    /// <summary>Name and version as the engine reports it.</summary>
    string Name { get; }

    Task<EngineResult> AnalyseAsync(string fen, int depth, CancellationToken cancellationToken);
}

public interface IChessEngineFactory
{
    /// <exception cref="System.ComponentModel.Win32Exception">The engine executable was not found.</exception>
    Task<IChessEngine> StartAsync(CancellationToken cancellationToken);
}

public class StockfishEngineFactory(IOptions<StockfishOptions> options) : IChessEngineFactory
{
    public async Task<IChessEngine> StartAsync(CancellationToken cancellationToken) =>
        await StockfishEngine.StartAsync(options.Value, cancellationToken);
}

/// <summary>Talks to a Stockfish process over the UCI protocol (stdin/stdout).</summary>
public sealed class StockfishEngine : IChessEngine
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly Process _process;

    private StockfishEngine(Process process) => _process = process;

    public string Name { get; private set; } = "Stockfish";

    public static async Task<StockfishEngine> StartAsync(StockfishOptions options, CancellationToken cancellationToken)
    {
        var process = Process.Start(new ProcessStartInfo(options.Path)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"Could not start {options.Path}.");

        var engine = new StockfishEngine(process);
        try
        {
            await engine.SendAsync("uci");
            await engine.ReadUntilAsync(line =>
            {
                if (line.StartsWith("id name ", StringComparison.Ordinal)) engine.Name = line[8..].Trim();
                return line == "uciok";
            }, cancellationToken);

            var threads = options.Threads > 0 ? options.Threads : Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            await engine.SendAsync($"setoption name Threads value {threads}");
            await engine.SendAsync($"setoption name Hash value {Math.Max(16, options.HashMb)}");
            await engine.SendAsync("isready");
            await engine.ReadUntilAsync(line => line == "readyok", cancellationToken);
            return engine;
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }
    }

    public async Task<EngineResult> AnalyseAsync(string fen, int depth, CancellationToken cancellationToken)
    {
        var whiteToMove = fen.Split(' ')[1] == "w";
        Evaluation? evaluation = null;
        IReadOnlyList<string> line = [];

        await SendAsync($"position fen {fen}");
        await SendAsync($"go depth {depth}");
        string? bestMove = null;
        await ReadUntilAsync(output =>
        {
            if (output.StartsWith("bestmove", StringComparison.Ordinal))
            {
                var move = output.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                bestMove = move is null or "(none)" ? null : move;
                return true;
            }
            if (output.StartsWith("info", StringComparison.Ordinal) && ParseInfo(output, whiteToMove) is { } info)
                (evaluation, line) = info;
            return false;
        }, cancellationToken);

        // No legal move: checkmate (the side to move lost) or stalemate.
        evaluation ??= Position.FromFen(fen).InCheck ? new Evaluation(null, 0) : new Evaluation(0, null);
        if (bestMove is not null && (line.Count == 0 || line[0] != bestMove)) line = [bestMove];
        return new EngineResult(evaluation.Value, bestMove, line);
    }

    /// <summary>Reads "info depth 16 ... score cp -35 ... pv e2e4 e7e5": the score (made White's point of view)
    /// and the line. Lines with only a bound (lowerbound/upperbound) or another multipv line are skipped.</summary>
    private static (Evaluation, IReadOnlyList<string>)? ParseInfo(string output, bool whiteToMove)
    {
        var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var score = Array.IndexOf(parts, "score");
        if (score < 0 || score + 2 >= parts.Length) return null;
        if (parts.Contains("lowerbound") || parts.Contains("upperbound")) return null;
        var multiPv = Array.IndexOf(parts, "multipv");
        if (multiPv >= 0 && parts.ElementAtOrDefault(multiPv + 1) != "1") return null;
        if (!int.TryParse(parts[score + 2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)) return null;

        var sign = whiteToMove ? 1 : -1;
        var evaluation = parts[score + 1] switch
        {
            "cp" => new Evaluation(sign * value, null),
            "mate" => new Evaluation(null, sign * value),
            _ => (Evaluation?)null
        };
        if (evaluation is null) return null;

        var pv = Array.IndexOf(parts, "pv");
        IReadOnlyList<string> line = pv >= 0 ? parts[(pv + 1)..] : [];
        return (evaluation.Value, line);
    }

    private async Task SendAsync(string command)
    {
        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    private async Task ReadUntilAsync(Func<string, bool> done, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync(timeout.Token)
                       ?? throw new InvalidOperationException("Stockfish stopped unexpectedly.");
            if (done(line.Trim())) return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await SendAsync("quit");
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _process.WaitForExitAsync(wait.Token);
            }
        }
        catch
        {
            // Not responding: killed below.
        }
        finally
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }
    }
}

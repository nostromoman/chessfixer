using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Chess;

/// <summary>A half-move as read from a handwritten scoresheet: the most likely reading and other possible readings.</summary>
/// <param name="Confidence">How sure the reader is of <paramref name="Text"/>.</param>
public sealed record ReadMove(int Number, bool White, string Text, IReadOnlyList<string> Alternatives, MoveConfidence Confidence)
{
    /// <summary>The reader had no doubt about this move.</summary>
    public bool Certain => Confidence == MoveConfidence.High;
}

/// <summary>How a reconstructed move was found.</summary>
public enum MoveSource
{
    /// <summary>The reading itself was legal.</summary>
    AsRead,
    /// <summary>The reading was not legal, one of the other readings was.</summary>
    Alternative,
    /// <summary>No reading was legal; the reader chose this legal move when asked again.</summary>
    Repaired
}

/// <summary>A move that could only be read with doubt: what was read, and which move it became.</summary>
public sealed record UncertainMove(int Number, bool White, string San, string ReadAs);

/// <summary>The move where reconstructing stopped: no reading was a legal move (or a move was missing).</summary>
/// <param name="Unread">The readings from this move on, as read (not checked).</param>
public sealed record ReconstructionStop(int Number, bool White, string ReadAs, IReadOnlyList<string> Unread);

/// <summary>The game as far as it could be replayed: the moves in SAN, with the doubtful ones marked.</summary>
/// <param name="Sources">Per move in <paramref name="Moves"/>: how it was found.</param>
public sealed record ReconstructedGame(
    IReadOnlyList<string> Moves,
    IReadOnlyList<UncertainMove> Uncertain,
    ReconstructionStop? Stop,
    IReadOnlyList<MoveSource> Sources);

/// <summary>Asked when no reading of a move is legal: which of the legal moves is written there?</summary>
/// <param name="PlayedSoFar">The moves before it, in SAN.</param>
/// <param name="LegalMoves">The legal moves in the position, in SAN.</param>
public sealed record RepairQuestion(
    int Number, bool White, string ReadAs, IReadOnlyList<string> Alternatives,
    IReadOnlyList<string> PlayedSoFar, IReadOnlyList<string> LegalMoves);

/// <summary>
/// Turns the moves read from a handwritten scoresheet into a legal game. Every move is replayed on the board:
/// the first reading that is a legal move is taken (often only one of the possible readings is legal, which
/// settles the doubt). When none is legal, <c>repair</c> is asked which legal move is written there (at most
/// <see cref="MaxRepairs"/> times); when that does not help either, the game stops at that move.
/// </summary>
public static class ScoresheetReconstruction
{
    public const int MaxRepairs = 3;

    public static async Task<ReconstructedGame> ReconstructAsync(
        IReadOnlyList<ReadMove> read, Func<RepairQuestion, Task<string?>> repair, string startFen = Position.StartFen)
    {
        var position = Position.FromFen(startFen);
        var moves = new List<string>();
        var uncertain = new List<UncertainMove>();
        var sources = new List<MoveSource>();
        var repairs = 0;

        for (var i = 0; i < read.Count; i++)
        {
            var current = read[i];
            ReconstructionStop Stop() => new(current.Number, current.White, current.Text,
                read.Skip(i).Select(m => $"{m.Number}.{(m.White ? "" : "..")} {m.Text}").ToList());

            // A half-move that was skipped or read twice: the colors no longer match the board.
            if (current.White != position.WhiteToMove || current.Number != position.FullmoveNumber)
                return new ReconstructedGame(moves, uncertain, Stop(), sources);

            Move? move = null;
            var source = MoveSource.AsRead;
            var doubtful = !current.Certain;
            var readings = new[] { current.Text }.Concat(current.Alternatives)
                .Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
            for (var r = 0; r < readings.Count && move is null; r++)
            {
                try
                {
                    move = position.ParseSan(readings[r]);
                    doubtful |= r > 0;
                    if (r > 0) source = MoveSource.Alternative;
                }
                catch (FormatException)
                {
                    // try the next reading
                }
            }

            if (move is null && repairs < MaxRepairs)
            {
                repairs++;
                var legal = position.LegalMoves().Select(position.ToSan).ToList();
                var answer = (await repair(new RepairQuestion(current.Number, current.White, current.Text,
                    current.Alternatives, moves.ToList(), legal)))?.Trim();
                if (!string.IsNullOrEmpty(answer) && legal.Contains(answer))
                {
                    move = position.ParseSan(answer);
                    doubtful = true;
                    source = MoveSource.Repaired;
                }
            }

            if (move is not { } m) return new ReconstructedGame(moves, uncertain, Stop(), sources);

            var san = position.ToSan(m);
            if (doubtful) uncertain.Add(new UncertainMove(current.Number, current.White, san, current.Text));
            moves.Add(san);
            sources.Add(source);
            position = position.Play(m);
        }

        return new ReconstructedGame(moves, uncertain, null, sources);
    }
}

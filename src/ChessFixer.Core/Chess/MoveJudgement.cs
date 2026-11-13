using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Chess;

/// <summary>
/// An engine evaluation from White's point of view: centipawns (+100 is about a pawn up for White), or mate in
/// <see cref="Mate"/> moves (positive: White mates, negative: Black mates; 0: the side to move is checkmated).
/// </summary>
public readonly record struct Evaluation(int? Centipawns, int? Mate)
{
    /// <summary>White's chance to win, 0–100, as Lichess computes it from the centipawns.</summary>
    public double WhiteWinChance(bool whiteToMove)
    {
        if (Mate is { } mate)
            return mate switch { > 0 => 100, < 0 => 0, _ => whiteToMove ? 0 : 100 };
        var cp = Math.Clamp(Centipawns ?? 0, -1000, 1000);
        return 50 + 50 * (2 / (1 + Math.Exp(-0.00368208 * cp)) - 1);
    }
}

/// <summary>How good a move was, compared with the engine's best move in that position.</summary>
/// <param name="WinChanceLoss">How much the mover's chance to win dropped (percentage points, 0–100).</param>
/// <param name="Accuracy">The accuracy of this move, 0–100 (Lichess formula).</param>
public readonly record struct MoveJudgement(MoveQuality Quality, double WinChanceLoss, double Accuracy)
{
    /// <summary>
    /// Judges a move by the evaluations before and after it. Thresholds as on Lichess: a drop in the chance to win
    /// of 5, 10 or 15 percentage points is an inaccuracy, a mistake or a blunder.
    /// </summary>
    /// <param name="whiteMoved">The move was made by White.</param>
    /// <param name="playedBest">The move is the engine's best move.</param>
    public static MoveJudgement Judge(Evaluation before, Evaluation after, bool whiteMoved, bool playedBest)
    {
        var whiteBefore = before.WhiteWinChance(whiteToMove: whiteMoved);
        var whiteAfter = after.WhiteWinChance(whiteToMove: !whiteMoved);
        var loss = Math.Max(0, whiteMoved ? whiteBefore - whiteAfter : whiteAfter - whiteBefore);
        if (playedBest) loss = 0; // the engine found nothing better: differences are search noise

        var quality = loss switch
        {
            >= 15 => MoveQuality.Blunder,
            >= 10 => MoveQuality.Mistake,
            >= 5 => MoveQuality.Inaccuracy,
            _ when playedBest => MoveQuality.Best,
            _ => MoveQuality.Good
        };
        var accuracy = Math.Clamp(103.1668 * Math.Exp(-0.04354 * loss) - 3.1669 + 1, 0, 100);
        return new MoveJudgement(quality, loss, accuracy);
    }
}

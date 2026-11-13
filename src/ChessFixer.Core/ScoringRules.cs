using ChessFixer.Shared.Enums;

namespace ChessFixer.Core;

/// <param name="Absent">Points for a round the player was absent (marked as not present); usually 0.</param>
public record ScoringRules(double Win, double Draw, double Loss, double Bye, double Absent = 0)
{
    public static ScoringRules Default { get; } = new(1, 0.5, 0, 1);

    /// <summary>Points for White and Black. A forfeit scores the same as a normal win or loss.</summary>
    public (double White, double Black) PointsFor(GameResult result) => result switch
    {
        GameResult.WhiteWins or GameResult.WhiteWinsByForfeit => (Win, Loss),
        GameResult.BlackWins or GameResult.BlackWinsByForfeit => (Loss, Win),
        GameResult.Draw => (Draw, Draw),
        GameResult.Bye => (Bye, 0),
        GameResult.Absent => (Absent, 0),
        // No opponent (competition): no points.
        _ => (0, 0)
    };
}

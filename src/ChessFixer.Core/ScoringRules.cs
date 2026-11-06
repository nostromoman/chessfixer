using ChessFixer.Shared.Enums;

namespace ChessFixer.Core;

public record ScoringRules(double Win, double Draw, double Loss, double Bye)
{
    public static ScoringRules Default { get; } = new(1, 0.5, 0, 1);

    /// <summary>Points for White and Black. A forfeit scores the same as a normal win or loss.</summary>
    public (double White, double Black) PointsFor(GameResult result) => result switch
    {
        GameResult.WhiteWins or GameResult.WhiteWinsByForfeit => (Win, Loss),
        GameResult.BlackWins or GameResult.BlackWinsByForfeit => (Loss, Win),
        GameResult.Draw => (Draw, Draw),
        GameResult.Bye => (Bye, 0),
        _ => (0, 0)
    };
}

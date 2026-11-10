using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Rating;

/// <summary>
/// Elo rating changes, following the FIDE rules:
/// <list type="bullet">
/// <item>Expected score E = 1 / (1 + 10^(-D/400)), where D is the rating difference, capped at 400.</item>
/// <item>Change = K × (score − E), rounded to whole points.</item>
/// <item>K = 40 for a player with fewer than 30 rated games, 10 from rating 2400, otherwise 20.</item>
/// <item>Only games played on the board count: forfeits and byes do not change the rating.</item>
/// </list>
/// </summary>
public static class EloCalculator
{
    /// <summary>Rating of a player without a rating; also the default for a new player.</summary>
    public const int DefaultRating = 1200;

    public const int NewPlayerGames = 30;
    private const int MaxDifference = 400;

    /// <summary>Whether a game with this result changes the ratings.</summary>
    public static bool IsRated(GameResult result) =>
        result is GameResult.WhiteWins or GameResult.Draw or GameResult.BlackWins;

    public static int KFactor(int rating, int ratedGames) =>
        ratedGames < NewPlayerGames ? 40 : rating >= 2400 ? 10 : 20;

    /// <summary>Expected score (0..1) of a player against an opponent.</summary>
    public static double ExpectedScore(int rating, int opponentRating)
    {
        var difference = Math.Clamp(rating - opponentRating, -MaxDifference, MaxDifference);
        return 1 / (1 + Math.Pow(10, -difference / 400.0));
    }

    /// <summary>Rating changes of White and Black, or null when the result does not count for the rating.</summary>
    /// <param name="whiteGames">Number of rated games White played before this one (for the K-factor).</param>
    /// <param name="blackGames">Number of rated games Black played before this one.</param>
    public static (int White, int Black)? Changes(
        int whiteRating, int whiteGames, int blackRating, int blackGames, GameResult result)
    {
        if (!IsRated(result)) return null;

        var whiteScore = result switch
        {
            GameResult.WhiteWins => 1.0,
            GameResult.Draw => 0.5,
            _ => 0.0
        };
        var white = KFactor(whiteRating, whiteGames) * (whiteScore - ExpectedScore(whiteRating, blackRating));
        var black = KFactor(blackRating, blackGames) * (1 - whiteScore - ExpectedScore(blackRating, whiteRating));
        return ((int)Math.Round(white, MidpointRounding.AwayFromZero), (int)Math.Round(black, MidpointRounding.AwayFromZero));
    }
}

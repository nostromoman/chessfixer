using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Rating;

/// <summary>A player as the rating calculation sees them, just before a game.</summary>
/// <param name="Rating">Rating before the game.</param>
/// <param name="RatedGames">Number of rated games played before this one (for the K-factor).</param>
/// <param name="HasReached2400">Whether the rating ever was 2400 or more: K stays 10 from then on.</param>
/// <param name="IsJunior">Up to and including the year of the 18th birthday. False when the birth date is unknown.</param>
public readonly record struct RatedPlayer(int Rating, int RatedGames, bool HasReached2400 = false, bool IsJunior = false);

/// <summary>
/// Elo rating changes, following the FIDE Rating Regulations (B.02, as of 1 October 2025):
/// <list type="bullet">
/// <item>The expected score comes from the FIDE table (8.1.2), in hundredths. A rating difference of more than 400
/// counts as 400, except for players rated 2650 or more (8.3.1).</item>
/// <item>Change = K × (score − expected score), rounded to whole points, 0.5 away from zero.</item>
/// <item>K = 40 for a player with fewer than 30 rated games, and for a junior (up to the year they turn 18) rated
/// under 2300; K = 10 once the rating has reached 2400, also when it drops below again; otherwise K = 20.</item>
/// <item>Only games played on the board count: forfeits and byes do not change the rating.</item>
/// </list>
/// Unlike FIDE (monthly lists), the rating changes right after each game.
/// </summary>
public static class EloCalculator
{
    /// <summary>Rating of a player without a rating; also the default for a new player (the lowest FIDE rating).</summary>
    public const int DefaultRating = 1400;

    public const int NewPlayerGames = 30;
    public const int JuniorAge = 18;
    private const int MaxDifference = 400;
    private const int NoDifferenceCapFrom = 2650;

    /// <summary>
    /// FIDE table 8.1.2: the highest rating difference for each expected score of the higher rated player,
    /// from 0.50 (index 0) to 0.99 (index 49). A larger difference gives 1.00.
    /// </summary>
    private static readonly int[] MaxDifferenceForScore =
    [
        3, 10, 17, 25, 32, 39, 46, 53, 61, 68,
        76, 83, 91, 98, 106, 113, 121, 129, 137, 145,
        153, 162, 170, 179, 188, 197, 206, 215, 225, 235,
        245, 256, 267, 278, 290, 302, 315, 328, 344, 357,
        374, 391, 411, 432, 456, 484, 517, 559, 619, 735
    ];

    /// <summary>Whether a game with this result changes the ratings.</summary>
    public static bool IsRated(GameResult result) =>
        result is GameResult.WhiteWins or GameResult.Draw or GameResult.BlackWins;

    /// <summary>Whether a player born on this date is a junior in the given year (FIDE: until the end of the
    /// year of the 18th birthday). Unknown birth date: not a junior.</summary>
    public static bool IsJunior(DateOnly? birthDate, int year) =>
        birthDate is { } date && year - date.Year <= JuniorAge;

    public static int KFactor(RatedPlayer player) =>
        player.RatedGames < NewPlayerGames ? 40
        : player.IsJunior && player.Rating < 2300 ? 40
        : player.HasReached2400 || player.Rating >= 2400 ? 10
        : 20;

    /// <summary>Expected score (0..1, in hundredths) of a player against an opponent, from the FIDE table.</summary>
    public static decimal ExpectedScore(int rating, int opponentRating)
    {
        var difference = rating - opponentRating;
        if (rating < NoDifferenceCapFrom) difference = Math.Clamp(difference, -MaxDifference, MaxDifference);

        var index = Array.FindIndex(MaxDifferenceForScore, max => Math.Abs(difference) <= max);
        var higher = index < 0 ? 100 : 50 + index;
        return (difference >= 0 ? higher : 100 - higher) / 100m;
    }

    /// <summary>Rating changes of White and Black, or null when the result does not count for the rating.</summary>
    public static (int White, int Black)? Changes(RatedPlayer white, RatedPlayer black, GameResult result)
    {
        if (!IsRated(result)) return null;

        var whiteScore = result switch
        {
            GameResult.WhiteWins => 1m,
            GameResult.Draw => 0.5m,
            _ => 0m
        };
        return (Change(white, black, whiteScore), Change(black, white, 1 - whiteScore));
    }

    private static int Change(RatedPlayer player, RatedPlayer opponent, decimal score) =>
        (int)Math.Round(KFactor(player) * (score - ExpectedScore(player.Rating, opponent.Rating)),
            MidpointRounding.AwayFromZero);
}

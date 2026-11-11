using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Rating;

/// <summary>A player as the rating calculation sees them, just before a game.</summary>
/// <param name="Rating">Rating before the game.</param>
/// <param name="HasReached2400">Whether the rating ever was 2400 or more: K stays 10 from then on.</param>
/// <param name="IsJunior">Up to and including the year of the 18th birthday. False when the birth date is unknown.</param>
public readonly record struct RatedPlayer(int Rating, bool HasReached2400 = false, bool IsJunior = false);

/// <summary>
/// Elo rating changes, following the FIDE Rating Regulations (B.02, as of 1 October 2025):
/// <list type="bullet">
/// <item>The expected score comes from the FIDE table (8.1.2), in hundredths. A rating difference of more than 400
/// counts as 400, except for players rated 2650 or more (8.3.1).</item>
/// <item>Change = K × (score − expected score). Over a tournament the changes are added up and rounded once to
/// whole points, 0.5 away from zero.</item>
/// <item>K = 40 for a junior (up to the year they turn 18) rated under 2300; K = 10 once the rating has reached 2400,
/// also when it drops below again; otherwise K = 20.</item>
/// <item>Only games played on the board count: forfeits and byes do not change the rating.</item>
/// </list>
/// The ratings change when a tournament is closed (FIDE: per monthly list). Unlike FIDE, new players have no
/// provisional period with K = 40 (FIDE: until 30 games).
/// </summary>
public static class EloCalculator
{
    /// <summary>Rating of a player without a rating; also the default for a new player (the lowest FIDE rating).</summary>
    public const int DefaultRating = 1400;

    public const int JuniorAge = 18;
    public const int MaxDifference = 400;
    public const int NoDifferenceCapFrom = 2650;

    public const int DefaultK = 20;
    public const int JuniorK = 40;
    public const int JuniorMaxRating = 2300;
    public const int MasterK = 10;
    public const int MasterRating = 2400;

    /// <summary>
    /// FIDE table 8.1.2: the highest rating difference for each expected score of the higher rated player,
    /// from 0.50 (index 0) to 0.99 (index 49). A larger difference gives 1.00.
    /// </summary>
    public static IReadOnlyList<int> ExpectedScoreTable => MaxDifferenceForScore;

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

    public static int KFactor(RatedPlayer player) => KFactorWithReason(player).K;

    /// <summary>The K-factor and which rule gave it.</summary>
    public static (int K, KFactorReason Reason) KFactorWithReason(RatedPlayer player) =>
        player.IsJunior && player.Rating < JuniorMaxRating ? (JuniorK, KFactorReason.Junior)
        : player.Rating >= MasterRating ? (MasterK, KFactorReason.Master)
        : player.HasReached2400 ? (MasterK, KFactorReason.WasMaster)
        : (DefaultK, KFactorReason.Default);

    /// <summary>The rating difference used for the expected score: capped at 400 below rating 2650.</summary>
    public static int UsedDifference(int rating, int opponentRating) =>
        rating < NoDifferenceCapFrom
            ? Math.Clamp(rating - opponentRating, -MaxDifference, MaxDifference)
            : rating - opponentRating;

    /// <summary>Expected score (0..1, in hundredths) of a player against an opponent, from the FIDE table.</summary>
    public static decimal ExpectedScore(int rating, int opponentRating)
    {
        var difference = UsedDifference(rating, opponentRating);

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

    /// <summary>A rating change rounded to whole points, 0.5 away from zero.</summary>
    public static int Round(decimal change) => (int)Math.Round(change, MidpointRounding.AwayFromZero);

    /// <summary>Every step of the calculation for one player, for explaining it.</summary>
    public static RatingCalculation Explain(RatedPlayer player, RatedPlayer opponent, decimal score)
    {
        var (k, reason) = KFactorWithReason(player);
        var expected = ExpectedScore(player.Rating, opponent.Rating);
        var exact = k * (score - expected);
        var change = Round(exact);
        return new RatingCalculation(player.Rating, opponent.Rating, player.Rating - opponent.Rating,
            UsedDifference(player.Rating, opponent.Rating), expected, score, k, reason, exact, change);
    }

    private static int Change(RatedPlayer player, RatedPlayer opponent, decimal score) =>
        Explain(player, opponent, score).Change;
}

/// <param name="Difference">Own rating minus the opponent's.</param>
/// <param name="UsedDifference">The difference after the cap of 400.</param>
/// <param name="ExactChange">K × (score − expected), before rounding.</param>
public record RatingCalculation(
    int Rating, int OpponentRating, int Difference, int UsedDifference, decimal ExpectedScore, decimal Score,
    int K, KFactorReason KReason, decimal ExactChange, int Change);

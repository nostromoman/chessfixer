using ChessFixer.Core.Rating;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Tests.Rating;

public class EloCalculatorTests
{
    private static RatedPlayer New(int rating) => new(rating);
    private static RatedPlayer Junior(int rating) => new(rating, IsJunior: true);

    [Fact]
    public void EqualRatings_WinGivesHalfOfK()
    {
        Assert.Equal((10, -10), EloCalculator.Changes(New(1400), New(1400), GameResult.WhiteWins));
        Assert.Equal((-10, 10), EloCalculator.Changes(New(1500), New(1500), GameResult.BlackWins));
        Assert.Equal((20, -20), EloCalculator.Changes(Junior(1400), Junior(1400), GameResult.WhiteWins));
    }

    [Fact]
    public void EqualRatings_DrawChangesNothing() =>
        Assert.Equal((0, 0), EloCalculator.Changes(New(1600), New(1600), GameResult.Draw));

    [Fact]
    public void Draw_AgainstStrongerPlayer_GainsPoints()
    {
        var (white, black) = EloCalculator.Changes(New(1400), New(1600), GameResult.Draw)!.Value;
        Assert.Equal(5, white);   // 20 × (0.5 − 0.24) = 5.2
        Assert.Equal(-5, black);
    }

    [Theory]
    [InlineData(0, 0.50)]
    [InlineData(3, 0.50)]
    [InlineData(4, 0.51)]
    [InlineData(10, 0.51)]
    [InlineData(11, 0.52)]
    [InlineData(95, 0.63)]
    [InlineData(200, 0.76)]
    [InlineData(-200, 0.24)]
    [InlineData(400, 0.92)]
    public void ExpectedScore_ComesFromTheFideTable(int difference, double expected) =>
        Assert.Equal((decimal)expected, EloCalculator.ExpectedScore(2000 + difference, 2000));

    [Fact]
    public void FideTable_DiffersFromTheFormula()
    {
        // 10 points stronger, K = 40 (juniors): the formula gives 0.5144 (+19.42 → 19), the table 0.51 (+19.6 → 20).
        Assert.Equal((20, -20), EloCalculator.Changes(Junior(1410), Junior(1400), GameResult.WhiteWins));
    }

    [Fact]
    public void Difference_IsCappedAt400_BelowRating2650()
    {
        Assert.Equal(0.92m, EloCalculator.ExpectedScore(2400, 1400));
        Assert.Equal(0.08m, EloCalculator.ExpectedScore(1400, 2400));
        // From 2650 the real difference counts.
        Assert.Equal(1.00m, EloCalculator.ExpectedScore(2700, 1900));
        Assert.Equal(0.99m, EloCalculator.ExpectedScore(2700, 2050));
    }

    [Theory]
    [InlineData(1400, false, false, 20)]      // also a new player: no provisional K = 40
    [InlineData(1800, false, false, 20)]
    [InlineData(2400, false, false, 10)]
    [InlineData(2350, true, false, 10)]       // reached 2400 once: stays 10
    [InlineData(1800, false, true, 40)]       // junior under 2300
    [InlineData(2300, false, true, 20)]       // junior from 2300: normal K
    public void KFactor_FollowsFide(int rating, bool reached2400, bool junior, int expected) =>
        Assert.Equal(expected, EloCalculator.KFactor(new(rating, reached2400, junior)));

    [Theory]
    [InlineData(null, false)]
    [InlineData("2008-12-31", true)]   // turns 18 in 2026: junior until the end of the year
    [InlineData("2007-01-01", false)]
    public void IsJunior_UntilTheEndOfTheYearTheyTurn18(string? birthDate, bool expected) =>
        Assert.Equal(expected, EloCalculator.IsJunior(birthDate is null ? null : DateOnly.Parse(birthDate), 2026));

    [Fact]
    public void HalfPoint_IsRoundedAwayFromZero()
    {
        // K = 10, 35 points stronger: table 0.55 (33-39 points): 10 × (1 − 0.55) = 4.5 → 5.
        Assert.Equal((5, -5), EloCalculator.Changes(New(2435), New(2400), GameResult.WhiteWins));
    }

    [Theory]
    [InlineData(GameResult.NotPlayed)]
    [InlineData(GameResult.WhiteWinsByForfeit)]
    [InlineData(GameResult.BlackWinsByForfeit)]
    [InlineData(GameResult.Bye)]
    public void UnplayedGames_AreNotRated(GameResult result) =>
        Assert.Null(EloCalculator.Changes(New(1400), New(1400), result));
}

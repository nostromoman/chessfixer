using ChessFixer.Core.Rating;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Tests.Rating;

public class EloCalculatorTests
{
    [Fact]
    public void EqualRatings_WinGivesHalfOfK()
    {
        Assert.Equal((20, -20), EloCalculator.Changes(1200, 0, 1200, 0, GameResult.WhiteWins));
        Assert.Equal((-10, 10), EloCalculator.Changes(1500, 50, 1500, 50, GameResult.BlackWins));
    }

    [Fact]
    public void EqualRatings_DrawChangesNothing() =>
        Assert.Equal((0, 0), EloCalculator.Changes(1600, 0, 1600, 0, GameResult.Draw));

    [Fact]
    public void Draw_AgainstStrongerPlayer_GainsPoints()
    {
        var (white, black) = EloCalculator.Changes(1400, 40, 1600, 40, GameResult.Draw)!.Value;
        Assert.Equal(5, white);   // 20 × (0.5 − 0.24)
        Assert.Equal(-5, black);
    }

    [Fact]
    public void Difference_IsCappedAt400()
    {
        Assert.Equal(EloCalculator.ExpectedScore(2400, 2000), EloCalculator.ExpectedScore(2400, 1200));
        Assert.Equal(0.909, EloCalculator.ExpectedScore(2000, 1600), 3);
    }

    [Theory]
    [InlineData(1200, 0, 40)]
    [InlineData(2500, 29, 40)]
    [InlineData(1800, 30, 20)]
    [InlineData(2400, 30, 10)]
    public void KFactor_FollowsFide(int rating, int games, int expected) =>
        Assert.Equal(expected, EloCalculator.KFactor(rating, games));

    [Theory]
    [InlineData(GameResult.NotPlayed)]
    [InlineData(GameResult.WhiteWinsByForfeit)]
    [InlineData(GameResult.BlackWinsByForfeit)]
    [InlineData(GameResult.Bye)]
    public void UnplayedGames_AreNotRated(GameResult result) =>
        Assert.Null(EloCalculator.Changes(1200, 0, 1200, 0, result));
}

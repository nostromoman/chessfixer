using ChessFixer.Core.Pairing;
using ChessFixer.Core.Standings;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Tests.Standings;

public class StandingsCalculatorTests
{
    private static IReadOnlyList<StandingRow> Calculate(int players, params PlayedGame[] games) =>
        StandingsCalculator.Calculate(Enumerable.Range(1, players), games, ScoringRules.Default);

    private static StandingRow Row(IReadOnlyList<StandingRow> rows, int playerId) =>
        rows.Single(r => r.PlayerId == playerId);

    [Fact]
    public void PointsBuchholzAndSonnebornBerger()
    {
        // R1: 1 beats 2, 3–4 draw.  R2: 1–3 draw, 4 beats 2.
        var rows = Calculate(4,
            new(1, 1, 2, GameResult.WhiteWins),
            new(1, 3, 4, GameResult.Draw),
            new(2, 1, 3, GameResult.Draw),
            new(2, 2, 4, GameResult.BlackWins));

        Assert.Equal(new StandingRow(1, 1, 1.5, 1, 0.5, 2, 1, 1, 0), Row(rows, 1));
        Assert.Equal(new StandingRow(1, 4, 1.5, 1, 0.5, 2, 1, 1, 0), Row(rows, 4));
        Assert.Equal(new StandingRow(3, 3, 1, 3, 1.5, 2, 0, 2, 0), Row(rows, 3));
        Assert.Equal(new StandingRow(4, 2, 0, 3, 0, 2, 0, 0, 2), Row(rows, 2));
    }

    [Fact]
    public void EqualPoints_HigherBuchholzRanksFirst()
    {
        // R1: 1 beats 2, 3 beats 4.  R2: 1 beats 4, 2 beats 3.
        // 2 and 3 both have 1 point; 2 met stronger opponents (Buchholz 3 vs 1).
        var rows = Calculate(4,
            new(1, 1, 2, GameResult.WhiteWins),
            new(1, 3, 4, GameResult.WhiteWins),
            new(2, 1, 4, GameResult.WhiteWins),
            new(2, 2, 3, GameResult.WhiteWins));

        Assert.Equal([1, 2, 3, 4], rows.Select(r => r.PlayerId));
        Assert.Equal([1, 2, 3, 4], rows.Select(r => r.Rank));
    }

    [Fact]
    public void EqualPointsAndBuchholz_HigherSonnebornBergerRanksFirst()
    {
        // 4 beat 1 (who scores 2), 3 beat 2 (who scores 1): equal points and Buchholz,
        // but 4 beat the stronger player, so 4 is ahead of 3 despite the higher id.
        var rows = Calculate(4,
            new(1, 4, 1, GameResult.WhiteWins),
            new(1, 3, 2, GameResult.WhiteWins),
            new(2, 2, 4, GameResult.WhiteWins),
            new(2, 1, 3, GameResult.WhiteWins),
            new(3, 1, 2, GameResult.WhiteWins),
            new(3, 4, 3, GameResult.Draw));

        Assert.Equal([1, 4, 3, 2], rows.Select(r => r.PlayerId));
        Assert.Equal(Row(rows, 4).Buchholz, Row(rows, 3).Buchholz);
        Assert.Equal(2.75, Row(rows, 4).SonnebornBerger);
        Assert.Equal(1.75, Row(rows, 3).SonnebornBerger);
        Assert.Equal([1, 2, 3, 4], rows.Select(r => r.Rank));
    }

    [Fact]
    public void Bye_CountsAsVirtualOpponentWithOwnPoints()
    {
        var rows = Calculate(3,
            new(1, 1, 2, GameResult.WhiteWins),
            new(1, 3, null, GameResult.Bye));

        var bye = Row(rows, 3);
        Assert.Equal(1, bye.Points);
        Assert.Equal(1, bye.Buchholz);        // virtual opponent with own score
        Assert.Equal(0, bye.SonnebornBerger); // not played on the board
        Assert.Equal(1, bye.Wins);
        Assert.Equal(0, Row(rows, 1).Buchholz);
    }

    [Fact]
    public void Forfeit_ScoresLikeAWin_ButCountsAsUnplayedForTieBreaks()
    {
        var rows = Calculate(2, new PlayedGame(1, 1, 2, GameResult.BlackWinsByForfeit));

        var winner = Row(rows, 2);
        Assert.Equal(1, winner.Points);
        Assert.Equal(1, winner.Buchholz);     // virtual opponent with own score
        Assert.Equal(0, winner.SonnebornBerger);
        Assert.Equal(0, Row(rows, 1).Points);
    }

    [Fact]
    public void GamesWithoutResult_AreIgnored()
    {
        var rows = Calculate(2, new PlayedGame(1, 1, 2, GameResult.NotPlayed));

        Assert.All(rows, r =>
        {
            Assert.Equal(0, r.Points);
            Assert.Equal(0, r.Played);
            Assert.Equal(1, r.Rank);
        });
    }

    [Fact]
    public void CustomScoring_IsUsedForPointsAndTieBreaks()
    {
        var scoring = new ScoringRules(Win: 3, Draw: 1, Loss: 0, Bye: 3);
        var rows = StandingsCalculator.Calculate([1, 2, 3, 4],
        [
            new(1, 1, 2, GameResult.WhiteWins),
            new(1, 3, 4, GameResult.Draw),
        ], scoring);

        Assert.Equal(3, Row(rows, 1).Points);
        Assert.Equal(1, Row(rows, 3).Points);
        Assert.Equal(1, Row(rows, 3).Buchholz);         // opponent 4 has 1 point (a draw)
        Assert.Equal(0.5, Row(rows, 3).SonnebornBerger); // drew against 1 point → ½ × 1
        Assert.Equal(0, Row(rows, 1).SonnebornBerger);   // beat player 2, who has 0 points
    }

    [Fact]
    public void PlayersWithoutGames_AndRemovedPlayers_AreListed()
    {
        // Player 3 joined late (no games), player 4 was removed after round 1.
        var rows = StandingsCalculator.Calculate([1, 2, 3, 4],
        [
            new(1, 1, 4, GameResult.BlackWins),
            new(2, 1, 2, GameResult.Draw),
        ], ScoringRules.Default);

        Assert.Equal(4, rows.Count);
        Assert.Equal(1, Row(rows, 4).Points);
        Assert.Equal(0, Row(rows, 3).Played);
    }
}

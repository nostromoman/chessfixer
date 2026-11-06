using ChessFixer.Core.Pairing;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Tests.Pairing;

public class SwissPairingTests
{
    private static TournamentSimulator Simulator(int players) =>
        new(new SwissPairing(), TournamentSimulator.CreatePlayers(players));

    [Fact]
    public void Round1_TopHalfPlaysBottomHalf_ByRating()
    {
        var pairings = Simulator(4).PairNext();

        Assert.Equal(
            [new BoardPairing(1, 1, 3), new BoardPairing(2, 4, 2)],
            pairings);
    }

    [Fact]
    public void Round1_PlayersWithoutRating_AreRankedLast()
    {
        var players = new List<PairingPlayer> { new(1, null), new(2, 1500), new(3, 1800), new(4, null) };
        var pairings = new TournamentSimulator(new SwissPairing(), players).PairNext();

        // Ranking: 3 (1800), 2 (1500), 1, 4 → 3 vs 1, 2 vs 4
        Assert.Contains(pairings, p => new[] { p.WhiteId, p.BlackId }.Order().SequenceEqual(new int?[] { 1, 3 }));
        Assert.Contains(pairings, p => new[] { p.WhiteId, p.BlackId }.Order().SequenceEqual(new int?[] { 2, 4 }));
    }

    [Fact]
    public void Round2_WinnersPlayWinners()
    {
        var sim = Simulator(8);
        sim.PlayRound();                              // 1, 2, 3, 4 win
        var round2 = sim.PairNext();

        var winners = new HashSet<int> { 1, 2, 3, 4 };
        foreach (var p in round2)
            Assert.Equal(winners.Contains(p.WhiteId), winners.Contains(p.BlackId!.Value));

        // Leaders on the top boards.
        Assert.Contains(round2[0].WhiteId, winners);
        Assert.Contains(round2[1].WhiteId, winners);
    }

    [Fact]
    public void OddNumberOfPlayers_LowestRankedGetsBye()
    {
        var pairings = Simulator(5).PairNext();

        var bye = Assert.Single(pairings, p => p.IsBye);
        Assert.Equal(5, bye.WhiteId);
        Assert.Equal(3, bye.Board); // bye on the last board
    }

    [Fact]
    public void NoPlayerGetsTwoByes_WhileOthersHaveNotHadOne()
    {
        var sim = Simulator(5);
        for (var round = 0; round < 5; round++) sim.PlayRound();

        var byes = sim.History.Where(g => g.BlackId is null).Select(g => g.WhiteId).ToList();
        Assert.Equal(5, byes.Count);
        Assert.Equal(5, byes.Distinct().Count());
    }

    [Theory]
    [InlineData(6, 5)]
    [InlineData(8, 6)]
    [InlineData(9, 7)]
    [InlineData(12, 7)]
    [InlineData(20, 9)]
    public void NoOneMeetsTheSameOpponentTwice(int players, int rounds)
    {
        var sim = Simulator(players);
        for (var round = 0; round < rounds; round++)
            sim.PlayRound(p => ((p.WhiteId * 7 + p.BlackId!.Value * 3 + round) % 3) switch
            {
                0 => GameResult.WhiteWins,
                1 => GameResult.Draw,
                _ => GameResult.BlackWins
            });

        var meetings = sim.Meetings().ToList();
        Assert.Equal(meetings.Count, meetings.Distinct().Count());
    }

    [Fact]
    public void EveryActivePlayerIsPairedExactlyOnce()
    {
        var sim = Simulator(11);
        for (var round = 0; round < 4; round++)
        {
            var ids = sim.PlayRound().SelectMany(p => new[] { p.WhiteId, p.BlackId }).OfType<int>().Order();
            Assert.Equal(Enumerable.Range(1, 11), ids);
        }
    }

    [Fact]
    public void ColorsAlternate()
    {
        var sim = Simulator(8);
        for (var round = 0; round < 5; round++) sim.PlayRound();

        foreach (var player in sim.Players)
        {
            var colors = sim.ColorsOf(player.Id);
            Assert.DoesNotContain("WWW", colors);
            Assert.DoesNotContain("BBB", colors);
            Assert.InRange(colors.Count(c => c == 'W') - colors.Count(c => c == 'B'), -1, 1);
        }
    }

    [Fact]
    public void RemovedPlayer_IsNotPaired_AndOpponentsDoNotMeetThemAgain()
    {
        var sim = Simulator(6);
        sim.PlayRound();
        sim.Players.RemoveAll(p => p.Id == 6);

        var round2 = sim.PlayRound();

        Assert.DoesNotContain(round2, p => p.WhiteId == 6 || p.BlackId == 6);
        Assert.Single(round2, p => p.IsBye);
    }

    [Fact]
    public void LateJoiner_IsPairedWithZeroPoints()
    {
        var sim = Simulator(4);
        sim.PlayRound();
        sim.Players.Add(new PairingPlayer(5, 2500)); // highest rating, but 0 points
        sim.Players.Add(new PairingPlayer(6, null));

        var round2 = sim.PairNext();

        // 1 and 2 won round 1 and stay on top; the late joiners play in the 0-point group.
        Assert.Equal(new[] { 1, 2 }, new[] { round2[0].WhiteId, round2[0].BlackId!.Value }.Order());
        Assert.Contains(round2, p => p.WhiteId == 5 || p.BlackId == 5);
        Assert.Equal(3, round2.Count);
    }

    [Fact]
    public void ForfeitedGame_DoesNotCountAsMet()
    {
        var players = TournamentSimulator.CreatePlayers(2);
        var history = new List<PlayedGame> { new(1, 1, 2, GameResult.WhiteWinsByForfeit) };

        var pairings = new SwissPairing().PairNextRound(new PairingInput(players, history, ScoringRules.Default));

        Assert.Single(pairings);
    }

    [Fact]
    public void ThrowsWhenNoValidPairingExists()
    {
        var players = TournamentSimulator.CreatePlayers(2);
        var history = new List<PlayedGame> { new(1, 1, 2, GameResult.Draw) };

        var error = Assert.Throws<PairingException>(() =>
            new SwissPairing().PairNextRound(new PairingInput(players, history, ScoringRules.Default)));
        Assert.Equal(PairingError.NoValidPairing, error.Error);
    }

    [Fact]
    public void ThrowsWithFewerThanTwoPlayers()
    {
        var error = Assert.Throws<PairingException>(() => Simulator(1).PairNext());
        Assert.Equal(PairingError.NotEnoughPlayers, error.Error);
    }

    [Fact]
    public void ScoringSettingsAreUsedForScoreGroups()
    {
        // With "3 points for a win, 1 for a draw" a winner (3) and two drawers (1 each) are in different groups.
        var scoring = new ScoringRules(Win: 3, Draw: 1, Loss: 0, Bye: 3);
        var players = TournamentSimulator.CreatePlayers(4);
        var history = new List<PlayedGame>
        {
            new(1, 1, 3, GameResult.WhiteWins),
            new(1, 4, 2, GameResult.Draw),
        };

        var pairings = new SwissPairing().PairNextRound(new PairingInput(players, history, scoring));

        // Player 1 (3 pts) plays the best-ranked drawer (2), not player 3 (0 pts, already met anyway).
        Assert.Equal(new[] { 1, 2 }, new[] { pairings[0].WhiteId, pairings[0].BlackId!.Value }.Order());
    }
}

using ChessFixer.Core.Pairing;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Tests.Pairing;

public class CompetitionPairingTests
{
    private static TournamentSimulator Simulator(int players, int times = 1)
    {
        var list = TournamentSimulator.CreatePlayers(players);
        var ids = list.Select(p => p.Id).ToList();
        return new TournamentSimulator(new CompetitionPairing(times, ids, ids), list);
    }

    /// <summary>Rounds for everyone to play everyone once: n − 1, or n with an odd number (everyone sits out once).</summary>
    private static int RoundsPerCycle(int players) => players % 2 == 0 ? players - 1 : players;

    private static List<(int, int)> Pairs(TournamentSimulator sim) => sim.Meetings().ToList();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(20)]
    public void EveryonePresent_EveryoneMeetsEveryoneOnce_InTheMinimalNumberOfRounds(int playerCount)
    {
        var sim = Simulator(playerCount);
        for (var round = 0; round < RoundsPerCycle(playerCount); round++) sim.PlayRound();

        var meetings = Pairs(sim);
        Assert.Equal(playerCount * (playerCount - 1) / 2, meetings.Count);
        Assert.Equal(meetings.Count, meetings.Distinct().Count());
        Assert.Throws<PairingException>(() => sim.PairNext());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    public void OddNumber_EveryoneSitsOutExactlyOnce(int playerCount)
    {
        var sim = Simulator(playerCount);
        for (var round = 0; round < RoundsPerCycle(playerCount); round++) sim.PlayRound();

        var sittingOut = sim.History.Where(g => g.Result == GameResult.NoOpponent).Select(g => g.WhiteId).Order();
        Assert.Equal(Enumerable.Range(1, playerCount), sittingOut);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    public void ColorsAreBalanced(int playerCount)
    {
        var sim = Simulator(playerCount);
        for (var round = 0; round < RoundsPerCycle(playerCount); round++) sim.PlayRound();

        foreach (var player in sim.Players)
        {
            var colors = sim.ColorsOf(player.Id);
            Assert.DoesNotContain("WWW", colors);
            Assert.DoesNotContain("BBB", colors);
            Assert.InRange(colors.Count(c => c == 'W') - colors.Count(c => c == 'B'), -1, 1);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(10)]
    public void TwiceAgainstEachOther_EveryPairPlaysOnceWithEachColor_ReturnGameInTheSecondHalf(int playerCount)
    {
        var sim = Simulator(playerCount, times: 2);
        var rounds = 2 * RoundsPerCycle(playerCount);
        for (var round = 0; round < rounds; round++) sim.PlayRound();

        var games = sim.History.Where(g => g.BlackId is not null)
            .GroupBy(g => (Math.Min(g.WhiteId, g.BlackId!.Value), Math.Max(g.WhiteId, g.BlackId!.Value)))
            .ToList();
        Assert.Equal(playerCount * (playerCount - 1) / 2, games.Count);
        Assert.All(games, pair =>
        {
            Assert.Equal(2, pair.Count());
            Assert.Equal(2, pair.Select(g => g.WhiteId).Distinct().Count());
            // The first game in the first cycle, the return game in the second.
            Assert.True(pair.Min(g => g.Round) <= rounds / 2 && pair.Max(g => g.Round) > rounds / 2);
        });
        Assert.Throws<PairingException>(() => sim.PairNext());
    }

    [Fact]
    public void ScoresAreIgnored()
    {
        var sim = Simulator(4);
        for (var round = 0; round < 3; round++) sim.PlayRound();
        Assert.Equal(6, Pairs(sim).Distinct().Count());
    }

    [Fact]
    public void OnlyPresentPlayersArePaired_AndOnlyPairsWithAGameLeft()
    {
        var sim = Simulator(6);
        sim.PlayRound();
        var present = sim.Players.Where(p => p.Id != 2).ToList();
        var pairings = sim.PlayRound(present: present);

        Assert.DoesNotContain(pairings, p => p.WhiteId == 2 || p.BlackId == 2);
        Assert.Equal(2, pairings.Count); // 5 present: 2 games, one player without opponent
        Assert.Equal(Pairs(sim).Count, Pairs(sim).Distinct().Count());
    }

    [Fact]
    public void PlayerWithMostGamesLeft_IsNotTheOneLeftOut()
    {
        // Player 6 missed two rounds: with 5 present, someone else sits out.
        var sim = Simulator(6);
        var withoutSix = sim.Players.Where(p => p.Id != 6).ToList();
        sim.PlayRound(present: withoutSix);
        sim.PlayRound(present: withoutSix);
        var withoutOne = sim.Players.Where(p => p.Id != 1).ToList();

        var pairings = sim.PairNext(withoutOne);

        Assert.Contains(pairings, p => p.WhiteId == 6 || p.BlackId == 6);
    }

    [Fact]
    public void PlayerLeftOutBefore_PlaysFirst()
    {
        // 3 players always present: the one left out rotates.
        var sim = Simulator(3, times: 2);
        var leftOut = new List<int>();
        for (var round = 0; round < 6; round++)
        {
            var pairing = Assert.Single(sim.PlayRound());
            leftOut.Add(sim.Players.Select(p => p.Id).Single(id => id != pairing.WhiteId && id != pairing.BlackId));
        }
        Assert.All(leftOut.GroupBy(x => x), g => Assert.Equal(2, g.Count()));
    }

    [Fact]
    public void Forfeit_UsesUpTheGame()
    {
        var sim = Simulator(2);
        sim.PlayRound(_ => GameResult.WhiteWinsByForfeit);
        var error = Assert.Throws<PairingException>(() => sim.PairNext());
        Assert.Equal(PairingError.CompetitionComplete, error.Error);
    }

    [Fact]
    public void NoGamesLeftBetweenThePresentPlayers()
    {
        var sim = Simulator(3);
        var oneAndTwo = sim.Players.Where(p => p.Id != 3).ToList();
        sim.PlayRound(present: oneAndTwo);

        var error = Assert.Throws<PairingException>(() => sim.PairNext(oneAndTwo));
        Assert.Equal(PairingError.NoGamesBetweenPresentPlayers, error.Error);
    }

    [Fact]
    public void LateJoiner_GetsGamesAgainstEveryone()
    {
        var list = TournamentSimulator.CreatePlayers(4);
        var ids = list.Select(p => p.Id).ToList();
        var sim = new TournamentSimulator(new CompetitionPairing(1, ids, ids), list);
        sim.PlayRound();
        sim.PlayRound();

        // Player 5 joins: a new strategy with the new participant list (as the server builds one per pairing).
        sim.Players.Add(new PairingPlayer(5, 1900));
        ids.Add(5);
        var rounds = 0;
        while (rounds < 10)
        {
            try { sim.PlayRound(); }
            catch (PairingException) { break; }
            rounds++;
        }

        Assert.Equal(10, Pairs(sim).Distinct().Count());
        Assert.Equal(10, Pairs(sim).Count);
    }

    [Theory]
    [InlineData(8, 1, 0.2)]
    [InlineData(12, 2, 0.15)]
    [InlineData(17, 2, 0.25)]
    [InlineData(20, 2, 0.1)]
    public void RandomAbsences_NoPairPlaysTooOften_AndTheSeasonCanBeFinished(int playerCount, int times, double absence)
    {
        for (var seed = 0; seed < 5; seed++)
        {
            var random = new Random(seed);
            var sim = Simulator(playerCount, times);
            var total = times * playerCount * (playerCount - 1) / 2;

            for (var round = 0; round < 200 && Pairs(sim).Count < total; round++)
            {
                var present = sim.Players.Where(_ => random.NextDouble() >= absence).ToList();
                if (present.Count < 2) continue;
                try { sim.PlayRound(present: present); }
                catch (PairingException e) when (e.Error == PairingError.NoGamesBetweenPresentPlayers) { }
            }

            var meetings = Pairs(sim);
            Assert.Equal(total, meetings.Count);
            Assert.All(meetings.GroupBy(m => m), g => Assert.Equal(times, g.Count()));
        }
    }
}

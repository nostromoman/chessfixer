using ChessFixer.Core.Pairing;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Tests.Pairing;

public class RoundRobinPairingTests
{
    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(5, 5)]
    [InlineData(10, 9)]
    public void TotalRounds(int players, int expectedRounds) =>
        Assert.Equal(expectedRounds, RoundRobinPairing.TotalRounds(players));

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
    public void EveryoneMeetsEveryoneExactlyOnce(int playerCount)
    {
        var sim = new TournamentSimulator(new RoundRobinPairing(), TournamentSimulator.CreatePlayers(playerCount));
        for (var round = 0; round < RoundRobinPairing.TotalRounds(playerCount); round++) sim.PlayRound();

        var meetings = sim.Meetings().ToList();
        Assert.Equal(playerCount * (playerCount - 1) / 2, meetings.Count);
        Assert.Equal(meetings.Count, meetings.Distinct().Count());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    public void OddNumberOfPlayers_EveryoneGetsExactlyOneBye(int playerCount)
    {
        var sim = new TournamentSimulator(new RoundRobinPairing(), TournamentSimulator.CreatePlayers(playerCount));
        for (var round = 0; round < RoundRobinPairing.TotalRounds(playerCount); round++) sim.PlayRound();

        var byes = sim.History.Where(g => g.BlackId is null).Select(g => g.WhiteId).Order();
        Assert.Equal(Enumerable.Range(1, playerCount), byes);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    public void ColorsAreBalanced(int playerCount)
    {
        var sim = new TournamentSimulator(new RoundRobinPairing(), TournamentSimulator.CreatePlayers(playerCount));
        for (var round = 0; round < RoundRobinPairing.TotalRounds(playerCount); round++) sim.PlayRound();

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
    public void AfterAllRounds_TheScheduleStartsAgain(int playerCount)
    {
        var sim = new TournamentSimulator(new RoundRobinPairing(), TournamentSimulator.CreatePlayers(playerCount));
        for (var round = 0; round < 2 * RoundRobinPairing.TotalRounds(playerCount); round++) sim.PlayRound();

        // Every pair has met exactly twice.
        var meetings = sim.Meetings().GroupBy(m => m).ToList();
        Assert.Equal(playerCount * (playerCount - 1) / 2, meetings.Count);
        Assert.All(meetings, m => Assert.Equal(2, m.Count()));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    public void SecondCycle_EveryPairPlaysOnceWithEachColor(int playerCount)
    {
        var sim = new TournamentSimulator(new RoundRobinPairing(), TournamentSimulator.CreatePlayers(playerCount));
        for (var round = 0; round < 2 * RoundRobinPairing.TotalRounds(playerCount); round++) sim.PlayRound();

        var notReversed = sim.History.Where(g => g.BlackId is not null)
            .GroupBy(g => (Math.Min(g.WhiteId, g.BlackId!.Value), Math.Max(g.WhiteId, g.BlackId!.Value)))
            .Where(g => g.Select(x => x.WhiteId).Distinct().Count() != 2)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(notReversed);
    }

    // ----- Players joining and leaving between rounds -----

    private static TournamentSimulator Simulator(int players) =>
        new(new RoundRobinPairing(), TournamentSimulator.CreatePlayers(players));

    private static int PairsMet(TournamentSimulator sim) => sim.Meetings().Distinct().Count();

    [Fact]
    public void Withdrawal_RemainingPlayersStillMeetEveryone()
    {
        var sim = Simulator(6);
        sim.PlayRound();
        sim.Players.RemoveAll(p => p.Id == 3);

        // 5 players left: a full schedule is 5 rounds.
        for (var round = 0; round < 5; round++) sim.PlayRound();

        var remaining = sim.Meetings().Where(m => m.A != 3 && m.B != 3).Distinct().Count();
        Assert.Equal(10, remaining);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Forfeits_CountAsScheduledGames_SoTheScheduleDoesNotRepeatThem(int playerCount)
    {
        // Player 1 withdrew before the first round: all their games are forfeits (as the server does).
        var sim = Simulator(playerCount);
        for (var round = 0; round < RoundRobinPairing.TotalRounds(playerCount); round++)
            sim.PlayRound(p => p.WhiteId == 1 ? GameResult.BlackWinsByForfeit
                : p.BlackId == 1 ? GameResult.WhiteWinsByForfeit
                : GameResult.Draw);

        var meetings = sim.Meetings().ToList();
        Assert.Equal(playerCount * (playerCount - 1) / 2, meetings.Count);
        Assert.Equal(meetings.Count, meetings.Distinct().Count());
    }

    [Fact]
    public void ScoresAreIgnored()
    {
        // Player 1 wins every game; in a Swiss it would meet the other leader, here it meets whoever it has not met.
        var sim = Simulator(4);
        for (var round = 0; round < 3; round++) sim.PlayRound();

        Assert.Equal(6, PairsMet(sim));
        Assert.Equal(6, sim.Meetings().Count());
    }

    [Fact]
    public void LateJoiner_EveryoneMeetsEveryoneWithinOneSchedule()
    {
        var sim = Simulator(4);
        sim.PlayRound();
        sim.PlayRound();
        sim.Players.Add(new PairingPlayer(5, 1900));

        // 5 players: a full schedule is 5 rounds.
        for (var round = 0; round < 5; round++) sim.PlayRound();

        Assert.Equal(10, PairsMet(sim));
        var opponentsOf5 = sim.Meetings().Where(m => m.B == 5).Select(m => m.A).Distinct();
        Assert.Equal([1, 2, 3, 4], opponentsOf5.Order());
    }

    [Fact]
    public void OddGroup_ByesAreSpreadEvenly()
    {
        var sim = Simulator(5);
        for (var round = 0; round < 10; round++) sim.PlayRound();

        var byes = sim.History.Where(g => g.BlackId is null).GroupBy(g => g.WhiteId).Select(g => g.Count()).ToList();
        Assert.Equal(5, byes.Count);
        Assert.All(byes, count => Assert.Equal(2, count));
    }
}

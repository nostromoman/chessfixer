using ChessFixer.Core.Pairing;

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

    [Fact]
    public void ThrowsWhenAllRoundsArePlayed()
    {
        var sim = new TournamentSimulator(new RoundRobinPairing(), TournamentSimulator.CreatePlayers(4));
        for (var round = 0; round < 3; round++) sim.PlayRound();

        var error = Assert.Throws<PairingException>(() => sim.PairNext());
        Assert.Equal(PairingError.AllRoundsPlayed, error.Error);
    }
}

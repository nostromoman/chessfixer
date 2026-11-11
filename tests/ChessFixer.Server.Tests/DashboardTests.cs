using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class DashboardTests : ApiTestBase
{
    [Fact]
    public async Task EmptySystem_HasNoFigures()
    {
        var dashboard = await GetAsync<DashboardDto>("api/dashboard");

        Assert.Equal((0, 0, 0, 0, null), (dashboard.Tournaments, dashboard.Players, dashboard.GamesPlayed, dashboard.Clubs, dashboard.AverageRating));
        Assert.Equal(12, dashboard.GamesPerMonth.Count);
        Assert.All(dashboard.GamesPerMonth, m => Assert.Equal(0, m.Games));
        Assert.Empty(dashboard.OpenTournaments);
        Assert.Empty(dashboard.RecentlyClosed);
    }

    [Fact]
    public async Task ShowsFiguresOpenAndClosedTournaments()
    {
        // A closed tournament: four players, one round where White won both games.
        var closed = await CreateTournamentAsync();
        await AddNewPlayersAsync(closed, 4);
        await StartRoundAsync(closed);
        await FinishActiveRoundAsync(closed, GameResult.WhiteWins);
        await PostAsync<TournamentDto>($"api/tournaments/{closed}/close", null);

        // An open tournament with its first round in progress.
        var open = await CreateTournamentAsync();
        await AddNewPlayersAsync(open, 2);
        await StartRoundAsync(open);

        var dashboard = await GetAsync<DashboardDto>("api/dashboard");

        Assert.Equal((2, 1, 6, 2), (dashboard.Tournaments, dashboard.ClosedTournaments, dashboard.Players, dashboard.GamesPlayed));
        Assert.Equal(new DashboardResultsDto(2, 0, 0, 0), dashboard.Results);
        Assert.Equal(2, dashboard.GamesPerMonth[^1].Games);
        Assert.Equal(5, dashboard.TopPlayers.Count);
        Assert.True(dashboard.TopPlayers.Select(p => p.Rating).SequenceEqual(dashboard.TopPlayers.Select(p => p.Rating).OrderDescending()));

        var running = Assert.Single(dashboard.OpenTournaments);
        Assert.Equal((open, TournamentState.RoundActive, 2, 1, 0, 1),
            (running.Id, running.State, running.Players, running.Round, running.GamesFinished, running.GamesInRound));

        var last = Assert.Single(dashboard.RecentlyClosed);
        Assert.Equal((closed, 4, 1), (last.Id, last.Players, last.Rounds));
        Assert.NotNull(last.Winner);
    }
}

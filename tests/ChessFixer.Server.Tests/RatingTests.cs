using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class RatingTests : ApiTestBase
{
    [Fact]
    public async Task NewPlayer_WithoutRating_Gets1400()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Magnus", null, null));
        Assert.Equal(1400, player.Rating);

        // Updating without a rating keeps the current one.
        var updated = await PutAsync<PlayerDto>($"api/players/{player.Id}", new UpdatePlayerRequest("Magnus", "Carlsen", null));
        Assert.Equal(1400, updated.Rating);
    }

    [Fact]
    public async Task InvalidRating_IsRejected() =>
        await AssertErrorAsync(
            await Client.PostAsJsonAsync("api/players", new CreatePlayerRequest("Magnus", null, -5)),
            HttpStatusCode.BadRequest, ApiErrorCode.InvalidRating);

    [Fact]
    public async Task RatedTournament_RatingChangesAfterEachGame()
    {
        var (id, white, black, game) = await StartGameAsync(rated: true);

        await SetResultAsync(id, game.Id, GameResult.WhiteWins);

        // Equal ratings, new players (K = 40): the winner gains 20.
        Assert.Equal(1420, await RatingOfAsync(white));
        Assert.Equal(1380, await RatingOfAsync(black));

        var history = await GetAsync<PlayerRatingDto>($"api/players/{white}/rating");
        Assert.Equal(1420, history.Rating);
        var change = Assert.Single(history.History);
        Assert.Equal((1400, 20, 1420), (change.RatingBefore, change.Change, change.RatingAfter));
        Assert.Equal(black, change.Opponent.PlayerId);
        Assert.Equal(1400, change.OpponentRating);
        Assert.True(change.IsWhite);
    }

    [Fact]
    public async Task CorrectedResult_RevertsThePreviousChange_ClearedResultRemovesIt()
    {
        var (id, white, black, game) = await StartGameAsync(rated: true);

        await SetResultAsync(id, game.Id, GameResult.WhiteWins);
        await SetResultAsync(id, game.Id, GameResult.BlackWins);
        Assert.Equal(1380, await RatingOfAsync(white));
        Assert.Equal(1420, await RatingOfAsync(black));
        Assert.Single((await GetAsync<PlayerRatingDto>($"api/players/{white}/rating")).History);

        await SetResultAsync(id, game.Id, GameResult.NotPlayed);
        Assert.Equal(1400, await RatingOfAsync(white));
        Assert.Equal(1400, await RatingOfAsync(black));
        Assert.Empty((await GetAsync<PlayerRatingDto>($"api/players/{white}/rating")).History);
    }

    [Fact]
    public async Task Forfeit_DoesNotChangeRating()
    {
        var (id, white, _, game) = await StartGameAsync(rated: true);
        await SetResultAsync(id, game.Id, GameResult.WhiteWinsByForfeit);
        Assert.Equal(1400, await RatingOfAsync(white));
    }

    [Fact]
    public async Task UnratedTournament_DoesNotChangeRating()
    {
        var (id, white, black, game) = await StartGameAsync(rated: false);
        await SetResultAsync(id, game.Id, GameResult.WhiteWins);
        Assert.Equal(1400, await RatingOfAsync(white));
        Assert.Equal(1400, await RatingOfAsync(black));
    }

    [Fact]
    public async Task IsRated_IsLockedAfterRound1()
    {
        var (id, _, _, game) = await StartGameAsync(rated: true);
        await SetResultAsync(id, game.Id, GameResult.Draw);

        var tournament = await GetTournamentAsync(id);
        Assert.True(tournament.Settings.IsRated);
        var response = await Client.PutAsJsonAsync($"api/tournaments/{id}",
            new UpdateTournamentRequest(tournament.Title, tournament.Settings with { IsRated = false }));
        await AssertErrorAsync(response, HttpStatusCode.Conflict, ApiErrorCode.SettingsLocked);
    }

    /// <summary>A tournament with two new players whose first game is under way.</summary>
    private async Task<(int TournamentId, int White, int Black, GameDto Game)> StartGameAsync(bool rated)
    {
        var tournament = await PostAsync<TournamentDto>("api/tournaments",
            new CreateTournamentRequest("Test", TournamentSettingsDto.Default with { IsRated = rated }));
        Assert.Equal(rated, tournament.Settings.IsRated);

        foreach (var name in new[] { "Wit", "Zwart" })
        {
            var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest(name, null, null));
            await PostAsync<TournamentDto>($"api/tournaments/{tournament.Id}/players", new AddTournamentPlayerRequest(player.Id));
        }
        await StartRoundAsync(tournament.Id);

        var game = (await GetRoundsAsync(tournament.Id)).Single(r => r.IsStarted).Games.Single();
        return (tournament.Id, game.White.PlayerId, game.Black!.PlayerId, game);
    }

    private async Task<int> RatingOfAsync(int playerId) =>
        (await GetAsync<List<PlayerDto>>("api/players")).Single(p => p.Id == playerId).Rating;
}

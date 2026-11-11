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

        // Equal ratings (K = 20): the winner gains 10.
        Assert.Equal(1410, await RatingOfAsync(white));
        Assert.Equal(1390, await RatingOfAsync(black));

        var history = await GetAsync<PlayerRatingDto>($"api/players/{white}/rating");
        Assert.Equal(1410, history.Rating);
        var change = Assert.Single(history.History);
        Assert.Equal((1400, 10, 1410), (change.RatingBefore, change.Change, change.RatingAfter));
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
        Assert.Equal(1390, await RatingOfAsync(white));
        Assert.Equal(1410, await RatingOfAsync(black));
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

    [Fact]
    public async Task Rules_AreTheValuesUsed()
    {
        var rules = await GetAsync<RatingRulesDto>("api/ratings/rules");
        Assert.Equal((1400, 20, 40, 10, 400), (rules.DefaultRating, rules.DefaultK, rules.JuniorK, rules.MasterK, rules.MaxDifference));
        Assert.Equal(new ExpectedScoreRangeDto(0, 3, 0.50m), rules.ExpectedScoreTable[0]);
        Assert.Equal(new ExpectedScoreRangeDto(4, 10, 0.51m), rules.ExpectedScoreTable[1]);
        Assert.Equal(new ExpectedScoreRangeDto(736, null, 1.00m), rules.ExpectedScoreTable[^1]);
    }

    [Fact]
    public async Task Calculate_ShowsEveryStep()
    {
        // 1500 loses to 1400: difference +100 → 0.64 expected, K 20 × (0 − 0.64) = −12.8 → −13.
        var calc = await GetAsync<RatingCalculationDto>(
            "api/ratings/calculate?whiteRating=1500&blackRating=1400&result=BlackWins&blackJunior=true");
        Assert.Equal((100, 0.64m, 0m, 20, KFactorReason.Default, -12.8m, -13, 1487),
            (calc.White.UsedDifference, calc.White.ExpectedScore, calc.White.Score, calc.White.K, calc.White.KReason,
             calc.White.ExactChange, calc.White.Change, calc.White.NewRating));
        // The junior has K = 40: 40 × (1 − 0.36) = 25.6 → 26.
        Assert.Equal((40, KFactorReason.Junior, 26), (calc.Black.K, calc.Black.KReason, calc.Black.Change));

        // The difference is capped at 400.
        var capped = await GetAsync<RatingCalculationDto>("api/ratings/calculate?whiteRating=2000&blackRating=1400&result=Draw");
        Assert.Equal((600, 400), (capped.White.Difference, capped.White.UsedDifference));
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

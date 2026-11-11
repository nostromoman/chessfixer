using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class RatingLogTests : ApiTestBase
{
    [Fact]
    public async Task GameWithAGuest_IsNotRated()
    {
        var tournament = await CreateTournamentAsync();
        var own = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Eigen", null, null));
        var guest = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Gast", null, null, IsGuest: true));
        foreach (var player in new[] { own, guest })
            await PostAsync<TournamentDto>($"api/tournaments/{tournament}/players", new AddTournamentPlayerRequest(player.Id));
        await StartRoundAsync(tournament);
        var game = (await GetRoundsAsync(tournament)).Single(r => r.IsStarted).Games.Single();

        await SetResultAsync(tournament, game.Id, GameResult.WhiteWins);
        await CloseAsync(tournament);

        var players = await GetAsync<List<PlayerDto>>("api/players");
        Assert.All(players, p => Assert.Equal(1400, p.Rating));
        Assert.DoesNotContain((await GetLogAsync()).Entries, e => e.Reason == RatingChangeReason.Tournament);

        // The game is in the history, marked as not counting.
        var history = await GetAsync<PlayerRatingDto>($"api/players/{own.Id}/rating");
        var played = Assert.Single(Assert.Single(history.Tournaments).Games);
        Assert.False(played.CountsForRating);
        Assert.Null(played.Change);
    }

    [Fact]
    public async Task RatingLog_RecordsEveryChange()
    {
        var tournament = await CreateTournamentAsync();
        var players = await AddDefaultPlayersAsync(tournament);
        await StartRoundAsync(tournament);
        var game = (await GetRoundsAsync(tournament)).Single(r => r.IsStarted).Games.Single();
        await SetResultAsync(tournament, game.Id, GameResult.WhiteWins);

        // Nothing changes during the tournament.
        var white = game.White.PlayerId;
        Assert.Equal([RatingChangeReason.NewPlayer], (await GetLogAsync(white)).Entries.Select(e => e.Reason));

        await CloseAsync(tournament);
        var closed = (await GetLogAsync(white)).Entries[0];
        Assert.Equal((RatingChangeReason.Tournament, 1400, 1410, tournament, 1, LeaderDisplayName),
            (closed.Reason, closed.RatingBefore, closed.RatingAfter, closed.TournamentId, closed.Games, closed.ChangedBy));

        // A rating set by hand is logged too.
        await PutAsync<PlayerDto>($"api/players/{white}", new UpdatePlayerRequest("Speler", null, 1500));
        var manual = (await GetLogAsync(white)).Entries[0];
        Assert.Equal((RatingChangeReason.Manual, 1410, 1500), (manual.Reason, manual.RatingBefore, manual.RatingAfter));

        // All players: newest first, paged.
        var page = await GetLogAsync(take: 2);
        Assert.Equal(2, page.Entries.Count);
        Assert.True(page.Total > 2);
        Assert.Contains(players, id => id == white);
    }

    [Fact]
    public async Task RatingList_ShowsOwnPlayersWithTrend()
    {
        var tournament = await CreateTournamentAsync();
        await AddDefaultPlayersAsync(tournament);
        await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Gast", null, null, IsGuest: true));
        await StartRoundAsync(tournament);
        var game = (await GetRoundsAsync(tournament)).Single(r => r.IsStarted).Games.Single();
        await SetResultAsync(tournament, game.Id, GameResult.WhiteWins);
        await CloseAsync(tournament);

        var list = await GetAsync<List<RatingListEntryDto>>("api/ratings");
        Assert.Equal(2, list.Count);  // no guest
        var winner = list[0];
        Assert.Equal((game.White.PlayerId, 1410, 1, 10), (winner.PlayerId, winner.Rating, winner.RatedGames, winner.Change30Days));
        Assert.Equal([1400, 1400, 1410], winner.Trend);
        Assert.Equal(-10, list[1].Change30Days);
    }

    private Task<TournamentDto> CloseAsync(int tournament) =>
        PostAsync<TournamentDto>($"api/tournaments/{tournament}/close", null);

    /// <summary>New players with the default rating (1400) in the tournament.</summary>
    private async Task<List<int>> AddDefaultPlayersAsync(int tournament, int count = 2)
    {
        var ids = new List<int>();
        for (var i = 1; i <= count; i++)
        {
            var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Speler", $"{i}", null));
            await PostAsync<TournamentDto>($"api/tournaments/{tournament}/players", new AddTournamentPlayerRequest(player.Id));
            ids.Add(player.Id);
        }
        return ids;
    }

    private Task<RatingLogPageDto> GetLogAsync(int? playerId = null, int take = 50) =>
        GetAsync<RatingLogPageDto>($"api/ratings/log?take={take}" + (playerId is { } id ? $"&playerId={id}" : ""));
}

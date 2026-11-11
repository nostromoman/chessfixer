using System.Net;
using System.Net.Http.Json;
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

        var players = await GetAsync<List<PlayerDto>>("api/players");
        Assert.All(players, p => Assert.Equal(1400, p.Rating));
        Assert.DoesNotContain((await GetLogAsync()).Entries, e => e.Reason == RatingChangeReason.Game);
    }

    [Fact]
    public async Task RatingLog_RecordsEveryChange()
    {
        var tournament = await CreateTournamentAsync();
        // Four players: the second game stays open, so the round can still be cancelled.
        var players = await AddDefaultPlayersAsync(tournament, 4);
        await StartRoundAsync(tournament);
        var game = (await GetRoundsAsync(tournament)).Single(r => r.IsStarted).Games.First(g => g.Board is not null);

        await SetResultAsync(tournament, game.Id, GameResult.WhiteWins);
        await SetResultAsync(tournament, game.Id, GameResult.Draw);  // correction: reverted, then the draw (no change)
        await SetResultAsync(tournament, game.Id, GameResult.BlackWins);
        await PostAsync<TournamentDto>($"api/tournaments/{tournament}/rounds/cancel", null);

        var white = game.White.PlayerId;
        var log = (await GetLogAsync(white)).Entries.Reverse().ToList();
        Assert.Equal(
        [
            (RatingChangeReason.NewPlayer, 1400, 1400),
            (RatingChangeReason.Game, 1400, 1410),
            (RatingChangeReason.GameReverted, 1410, 1400),
            (RatingChangeReason.Game, 1400, 1390),
            (RatingChangeReason.RoundCancelled, 1390, 1400)
        ], log.Select(e => (e.Reason, e.RatingBefore, e.RatingAfter)));

        var first = log[1];
        Assert.Equal((tournament, 1, game.Black!.PlayerId, 1400, GameResult.WhiteWins, true),
            (first.TournamentId, first.Round, first.Opponent!.PlayerId, first.OpponentRating, first.Result, first.IsWhite));
        Assert.Equal(GameResult.WhiteWins, log[2].Result);  // the result that was taken back
        Assert.Equal(LeaderDisplayName, first.ChangedBy);

        // A rating set by hand is logged too.
        await PutAsync<PlayerDto>($"api/players/{white}", new UpdatePlayerRequest("Speler", null, 1500));
        var manual = (await GetLogAsync(white)).Entries[0];
        Assert.Equal((RatingChangeReason.Manual, 1400, 1500), (manual.Reason, manual.RatingBefore, manual.RatingAfter));

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

        var list = await GetAsync<List<RatingListEntryDto>>("api/ratings");
        Assert.Equal(2, list.Count);  // no guest
        var winner = list[0];
        Assert.Equal((game.White.PlayerId, 1410, 1, 10), (winner.PlayerId, winner.Rating, winner.RatedGames, winner.Change30Days));
        Assert.Equal([1400, 1400, 1410], winner.Trend);
        Assert.Equal(-10, list[1].Change30Days);
    }

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

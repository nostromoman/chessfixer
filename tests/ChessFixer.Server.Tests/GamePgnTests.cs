using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class GamePgnTests : ApiTestBase
{
    private const string Pgn = "[White \"A\"]\r\n[Black \"B\"]\r\n\r\n1. e4 e5 2. Nf3 Nc6 1-0\r\n";

    private Task<GamePgnDto> GetPgnAsync(int id, int gameId) => GetAsync<GamePgnDto>($"api/tournaments/{id}/games/{gameId}/pgn");

    private Task<HttpResponseMessage> PutPgnAsync(int id, int gameId, string? pgn) =>
        Client.PutAsJsonAsync($"api/tournaments/{id}/games/{gameId}/pgn", new SetPgnRequest(pgn));

    [Fact]
    public async Task Pgn_AddedToPlayedGame_KeptAfterClosing_AndRemoved()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        await StartRoundAsync(id);
        var game = Assert.Single((await GetRoundsAsync(id))[0].Games);

        // Not played yet: no moves.
        await AssertErrorAsync(await PutPgnAsync(id, game.Id, Pgn), HttpStatusCode.Conflict, ApiErrorCode.PgnNotAllowed);

        await SetResultAsync(id, game.Id, GameResult.WhiteWins);
        Assert.False(Assert.Single((await GetRoundsAsync(id))[0].Games).HasPgn);
        Assert.Null((await GetPgnAsync(id, game.Id)).Pgn);

        await ReadAsync<TournamentDto>(await PutPgnAsync(id, game.Id, Pgn));
        Assert.True(Assert.Single((await GetRoundsAsync(id))[0].Games).HasPgn);
        Assert.Equal("[White \"A\"]\n[Black \"B\"]\n\n1. e4 e5 2. Nf3 Nc6 1-0", (await GetPgnAsync(id, game.Id)).Pgn);

        // Text without moves is refused.
        await AssertErrorAsync(await PutPgnAsync(id, game.Id, "[White \"A\"]"), HttpStatusCode.BadRequest, ApiErrorCode.InvalidPgn);

        // After closing the moves can still be changed: they do not count for anything.
        await PostAsync<TournamentDto>($"api/tournaments/{id}/close", null);
        await ReadAsync<TournamentDto>(await PutPgnAsync(id, game.Id, "1. d4 d5 1-0"));
        Assert.Equal("1. d4 d5 1-0", (await GetPgnAsync(id, game.Id)).Pgn);

        await ReadAsync<TournamentDto>(await PutPgnAsync(id, game.Id, "  "));
        Assert.Null((await GetPgnAsync(id, game.Id)).Pgn);
        Assert.False(Assert.Single((await GetRoundsAsync(id))[0].Games).HasPgn);
    }

    [Fact]
    public async Task Pgn_NotForForfeit_OrGameOfAnotherTournament()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        await StartRoundAsync(id);
        var game = Assert.Single((await GetRoundsAsync(id))[0].Games);
        await SetResultAsync(id, game.Id, GameResult.WhiteWinsByForfeit);

        await AssertErrorAsync(await PutPgnAsync(id, game.Id, Pgn), HttpStatusCode.Conflict, ApiErrorCode.PgnNotAllowed);

        var other = await CreateTournamentAsync();
        await AssertErrorAsync(await Client.GetAsync($"api/tournaments/{other}/games/{game.Id}/pgn"),
            HttpStatusCode.NotFound, ApiErrorCode.GameNotFound);
    }
}

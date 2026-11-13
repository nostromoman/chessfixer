using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class PlayerTests : ApiTestBase
{
    [Fact]
    public async Task CreateAndUpdate_FullNameIsComputed()
    {
        var created = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("  Magnus ", null, null));
        Assert.Equal("Magnus", created.FirstName);
        Assert.Equal("", created.LastName);
        Assert.Equal("Magnus", created.Name);

        var updated = await PutAsync<PlayerDto>($"api/players/{created.Id}", new UpdatePlayerRequest("Magnus", " Carlsen ", 2830));
        Assert.Equal("Carlsen", updated.LastName);
        Assert.Equal("Magnus Carlsen", updated.Name);
        Assert.Equal(2830, updated.Rating);

        var player = Assert.Single(await GetAsync<List<PlayerDto>>("api/players"));
        Assert.Equal(updated, player);
    }

    [Fact]
    public async Task OptionalDetails_AreStored()
    {
        var created = await PostAsync<PlayerDto>("api/players",
            new CreatePlayerRequest("Judit", "Polgar", 2735, new DateOnly(1976, 7, 23), " Boedapest "));
        Assert.Equal(new DateOnly(1976, 7, 23), created.BirthDate);
        Assert.Equal("Boedapest", created.City);
        Assert.False(created.IsGuest);
        Assert.Null(created.ClubId);
        Assert.Null(created.PhotoVersion);
    }

    [Fact]
    public async Task Photo_CanBeSetAndRemoved()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Magnus", null, null));
        byte[] image = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

        var content = new ByteArrayContent(image);
        content.Headers.ContentType = new("image/jpeg");
        var withPhoto = await ReadAsync<PlayerDto>(await Client.PutAsync($"api/players/{player.Id}/photo", content));
        Assert.NotNull(withPhoto.PhotoUrl);

        var photo = await Client.GetAsync(withPhoto.PhotoUrl);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(image, await photo.Content.ReadAsByteArrayAsync());

        var withoutPhoto = await ReadAsync<PlayerDto>(await Client.DeleteAsync($"api/players/{player.Id}/photo"));
        Assert.Null(withoutPhoto.PhotoUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"api/players/{player.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Photo_ThatIsNotAnImage_IsRejected()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Magnus", null, null));
        var response = await Client.PutAsync($"api/players/{player.Id}/photo", new StringContent("hello"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesPlayerFromList_ButKeepsIt()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Magnus", null, null));
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/players/{player.Id}")).StatusCode);

        Assert.Empty(await GetAsync<List<PlayerDto>>("api/players"));
        var deleted = Assert.Single(await GetAsync<List<PlayerDto>>("api/players?includeDeleted=true"));
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task Delete_PlayerInTournament_StaysInTournament_ButCannotJoinAgain()
    {
        var tournamentId = await CreateTournamentAsync();
        var playerIds = await AddNewPlayersAsync(tournamentId, 2);
        await StartRoundAsync(tournamentId);
        await FinishActiveRoundAsync(tournamentId);
        var deletedId = playerIds[0];

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/players/{deletedId}")).StatusCode);

        Assert.DoesNotContain(await GetAsync<List<PlayerDto>>("api/players"), p => p.Id == deletedId);
        // Still in the tournament, marked as deleted so the client can show "(verwijderd)".
        Assert.True((await GetStandingsAsync(tournamentId)).Single(s => s.PlayerId == deletedId).IsDeleted);
        Assert.False((await GetStandingsAsync(tournamentId)).Single(s => s.PlayerId == playerIds[1]).IsDeleted);
        Assert.True((await GetAsync<List<TournamentPlayerDto>>($"api/tournaments/{tournamentId}/players"))
            .Single(p => p.PlayerId == deletedId).IsDeleted);
        var game = (await GetRoundsAsync(tournamentId)).First(r => r.IsStarted).Games.Single(g => g.Black is not null);
        var deletedSide = game.White.PlayerId == deletedId ? game.White : game.Black!;
        Assert.True(deletedSide.IsDeleted);
        var opponent = (await GetAsync<PlayerStatisticsDto>($"api/players/{playerIds[1]}/statistics")).Games.Single().Opponent!;
        Assert.True(opponent.IsDeleted);

        var otherTournamentId = await CreateTournamentAsync();
        await AssertErrorAsync(
            await Client.PostAsJsonAsync($"api/tournaments/{otherTournamentId}/players", new AddTournamentPlayerRequest(deletedId)),
            HttpStatusCode.NotFound, ApiErrorCode.PlayerNotFound);
    }

    [Fact]
    public async Task Statistics_ShowTournamentResultsAndGames()
    {
        var tournamentId = await CreateTournamentAsync(PairingSystem.Swiss);
        var playerIds = await AddNewPlayersAsync(tournamentId, 3);
        await StartRoundAsync(tournamentId);
        var round = (await GetRoundsAsync(tournamentId)).Single(r => r.IsStarted);
        await FinishActiveRoundAsync(tournamentId, GameResult.Draw);

        foreach (var playerId in playerIds)
        {
            var statistics = await GetAsync<PlayerStatisticsDto>($"api/players/{playerId}/statistics");
            Assert.Equal(1, statistics.Tournaments);

            var result = Assert.Single(statistics.TournamentResults);
            Assert.Equal(tournamentId, result.TournamentId);
            Assert.Equal(3, result.PlayerCount);

            var game = Assert.Single(statistics.Games);
            Assert.Equal(1, game.Round);
            var roundGame = round.Games.Single(g => g.White.PlayerId == playerId || g.Black?.PlayerId == playerId);
            Assert.Equal(roundGame.White.PlayerId == playerId, game.IsWhite);
            if (game.Opponent is null)
            {
                Assert.Equal(GameResult.Bye, game.Result);
                Assert.Equal(1, game.Points);
            }
            else
            {
                Assert.Equal(GameResult.Draw, game.Result);
                Assert.Equal(0.5, game.Points);
                Assert.Equal(1, statistics.Draws);
            }
        }
    }

    [Fact]
    public async Task Statistics_PlayerWithoutTournaments_IsEmpty()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Magnus", null, null));
        var statistics = await GetAsync<PlayerStatisticsDto>($"api/players/{player.Id}/statistics");
        Assert.Equal(0, statistics.Tournaments);
        Assert.Empty(statistics.TournamentResults);
        Assert.Empty(statistics.Games);
    }

    [Fact]
    public async Task Create_WithoutFirstName_IsRejected()
    {
        var response = await Client.PostAsJsonAsync("api/players", new CreatePlayerRequest(" ", "Carlsen", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

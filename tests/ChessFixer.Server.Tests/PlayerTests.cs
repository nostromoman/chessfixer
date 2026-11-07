using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;

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
            new CreatePlayerRequest("Judit", "Polgar", 2735, new DateOnly(1976, 7, 23), " Boedapest ", "  "));
        Assert.Equal(new DateOnly(1976, 7, 23), created.BirthDate);
        Assert.Equal("Boedapest", created.City);
        Assert.Null(created.Club);
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
    public async Task Delete_RemovesPlayer()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Magnus", null, null));
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/players/{player.Id}")).StatusCode);
        Assert.Empty(await GetAsync<List<PlayerDto>>("api/players"));
    }

    [Fact]
    public async Task Delete_PlayerInTournament_IsRejected()
    {
        var tournamentId = await CreateTournamentAsync();
        var playerId = (await AddNewPlayersAsync(tournamentId, 1)).Single();

        var response = await Client.DeleteAsync($"api/players/{playerId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutFirstName_IsRejected()
    {
        var response = await Client.PostAsJsonAsync("api/players", new CreatePlayerRequest(" ", "Carlsen", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

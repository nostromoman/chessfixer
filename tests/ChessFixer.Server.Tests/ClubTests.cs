using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class ClubTests : ApiTestBase
{
    [Fact]
    public async Task Clubs_CanBeManaged_WithOneOwnClub()
    {
        var own = await PostAsync<ClubDto>("api/clubs", new SaveClubRequest(" SV De Toren ", " Utrecht ", "", true));
        Assert.Equal(("SV De Toren", "Utrecht", (string?)null, true), (own.Name, own.City, own.Description, own.IsOwnClub));

        // Marking another club as own club unmarks the first one.
        var other = await PostAsync<ClubDto>("api/clubs", new SaveClubRequest("Paard Sprong", null, "Gezellige club", true));
        var clubs = await GetAsync<List<ClubDto>>("api/clubs");
        Assert.Equal([other.Id, own.Id], clubs.Select(c => c.Id));
        Assert.Equal([true, false], clubs.Select(c => c.IsOwnClub));

        var renamed = await PutAsync<ClubDto>($"api/clubs/{own.Id}", new SaveClubRequest("De Toren", "Utrecht", null, false));
        Assert.Equal("De Toren", renamed.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/clubs/{own.Id}")).StatusCode);
        Assert.Single(await GetAsync<List<ClubDto>>("api/clubs"));
    }

    [Fact]
    public async Task Club_WithoutName_IsRejected() =>
        await AssertErrorAsync(
            await Client.PostAsJsonAsync("api/clubs", new SaveClubRequest("  ", null, null, false)),
            HttpStatusCode.BadRequest, ApiErrorCode.ClubNameRequired);

    [Fact]
    public async Task GuestPlayer_HasAClub_OwnPlayerDoesNot()
    {
        var club = await PostAsync<ClubDto>("api/clubs", new SaveClubRequest("Paard Sprong", null, null, false));

        var guest = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Gast", null, null, IsGuest: true, ClubId: club.Id));
        Assert.Equal((true, club.Id, "Paard Sprong"), (guest.IsGuest, guest.ClubId, guest.ClubName));
        Assert.Equal(1, (await GetAsync<List<ClubDto>>("api/clubs")).Single().GuestPlayers);

        // An own player has no club.
        var own = await PutAsync<PlayerDto>($"api/players/{guest.Id}", new UpdatePlayerRequest("Gast", null, null, IsGuest: false, ClubId: club.Id));
        Assert.Equal((false, (int?)null), (own.IsGuest, own.ClubId));

        await AssertErrorAsync(
            await Client.PostAsJsonAsync("api/players", new CreatePlayerRequest("X", null, null, IsGuest: true, ClubId: 999)),
            HttpStatusCode.NotFound, ApiErrorCode.ClubNotFound);
    }

    [Fact]
    public async Task DeletedClub_LeavesItsGuestsWithoutClub()
    {
        var club = await PostAsync<ClubDto>("api/clubs", new SaveClubRequest("Paard Sprong", null, null, false));
        var guest = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Gast", null, null, IsGuest: true, ClubId: club.Id));

        await Client.DeleteAsync($"api/clubs/{club.Id}");

        var player = (await GetAsync<List<PlayerDto>>("api/players")).Single(p => p.Id == guest.Id);
        Assert.True(player.IsGuest);
        Assert.Null(player.ClubId);
    }
}

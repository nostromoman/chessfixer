using System.Net;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class DevelopmentTests : ApiTestBase
{
    [Fact]
    public async Task TournamentLeader_CannotResetTheDatabase() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsync("api/development/reset", null)).StatusCode);

    [Fact]
    public async Task Reset_DeletesTournamentsPlayersAndClubs_KeepsAccounts_SeedsClubsAndPlayers()
    {
        var tournamentId = await CreateTournamentAsync();
        await AddNewPlayersAsync(tournamentId, 4);
        await StartRoundAsync(tournamentId);
        await FinishActiveRoundAsync(tournamentId);
        await PostAsync<TournamentDto>($"api/tournaments/{tournamentId}/close", null);

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var result = await ReadAsync<DevelopmentResetDto>(await admin.PostAsync("api/development/reset", null));
        Assert.Equal((30, 3), (result.Players, result.Clubs));
        Assert.NotNull(result.Backup);

        // The tournament leader can still log in; the data is new.
        using var leader = await CreateClientAsync(LeaderUserName, LeaderPassword);
        Assert.Empty(await GetAsync<List<TournamentSummaryDto>>("api/tournaments"));

        var clubs = await GetAsync<List<ClubDto>>("api/clubs");
        var own = Assert.Single(clubs, c => c.IsOwnClub);
        Assert.Equal("Schaakvereniging Oisterwijk", own.Name);

        var players = await GetAsync<List<PlayerDto>>("api/players");
        Assert.Equal(30, players.Count);
        var guests = players.Where(p => p.IsGuest).ToList();
        Assert.Equal(7, guests.Count);
        Assert.All(guests, g => Assert.Contains(clubs, c => c.Id == g.ClubId && !c.IsOwnClub));
        Assert.Equal(2, guests.Select(g => g.ClubId).Distinct().Count());
        Assert.Equal(5, players.Count(p => p.BirthDate?.Year >= 2010)); // the youth players
        Assert.Equal(5, players.Count(p => !p.IsGuest && p.BirthDate?.Year >= 2010));
    }
}

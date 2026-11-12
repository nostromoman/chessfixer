using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class ActivityTests : ApiTestBase
{
    [Fact]
    public async Task TournamentLeader_CannotSeeTheActivityLog()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("api/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("api/activity/users")).StatusCode);
    }

    [Fact]
    public async Task LoginsActionsAndRefusals_AreLogged_ReadingIsNot()
    {
        var tournamentId = await CreateTournamentAsync();
        await GetTournamentAsync(tournamentId); // reading is not logged
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client.PostAsJsonAsync("api/leaders", new CreateLeaderRequest("piet", "Piet", "Wachtwoord1"))).StatusCode);

        using (var anonymous = await CreateClientAsync())
        {
            await anonymous.PostAsJsonAsync("api/auth/login", new LoginRequest(LeaderUserName, "verkeerd!"));
            await anonymous.PostAsJsonAsync("api/auth/login", new LoginRequest("onbekend", "verkeerd!"));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsync("api/auth/logout", null)).StatusCode);

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var leaderId = (await ReadAsync<List<LeaderDto>>(await admin.GetAsync("api/leaders"))).Single().Id;

        var log = await ReadAsync<ActivityLogPageDto>(await admin.GetAsync($"api/activity?userId={leaderId}"));
        var entries = log.Entries.Reverse().Select(e => (e.Kind, e.Action, e.StatusCode)).ToList();
        Assert.Equal(
        [
            (ActivityKind.Login, null, null),
            (ActivityKind.Action, "Tournaments.Create", 201),
            (ActivityKind.Action, "Leaders.Create", 403),
            (ActivityKind.LoginFailed, null, null),
            (ActivityKind.Logout, null, null)
        ], entries);

        var created = log.Entries.Single(e => e.Action == "Tournaments.Create");
        Assert.Equal((LeaderUserName, LeaderDisplayName, "POST", "/api/tournaments"),
            (created.UserName, created.DisplayName, created.Method, created.Path));

        // A failed login with an unknown user name has no user, but keeps the name that was typed.
        var all = await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity?problemsOnly=true"));
        Assert.Equal(
        [
            (ActivityKind.LoginFailed, "onbekend", (int?)null),
            (ActivityKind.LoginFailed, LeaderUserName, leaderId),
            (ActivityKind.Action, LeaderUserName, leaderId)
        ], all.Entries.Select(e => (e.Kind, e.UserName, e.UserId)).ToList());
    }

    [Fact]
    public async Task ActionOnATournament_ShowsItsTitle()
    {
        var tournamentId = await CreateTournamentAsync();
        await AddNewPlayersAsync(tournamentId, 2);

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var log = await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity"));
        var entry = log.Entries.First(e => e.Action == "Tournaments.AddPlayer");
        Assert.Equal(("Tournaments.AddPlayer", tournamentId, "Test"), (entry.Action, entry.TournamentId, entry.TournamentTitle));
    }

    [Fact]
    public async Task Users_HaveASummaryOfTheirActivity()
    {
        await CreateTournamentAsync();
        using (var anonymous = await CreateClientAsync())
            await anonymous.PostAsJsonAsync("api/auth/login", new LoginRequest(LeaderUserName, "verkeerd!"));

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var users = await ReadAsync<List<ActivityUserDto>>(await admin.GetAsync("api/activity/users"));

        Assert.Equal([UserRole.TournamentLeader, UserRole.Admin], users.Select(u => u.Role).ToList());
        var leader = users[0];
        Assert.Equal((1, 1, 1, 0), (leader.Logins, leader.FailedLogins, leader.Actions, leader.FailedActions));
        Assert.NotNull(leader.LastLoginAt);
        Assert.NotNull(leader.LastSeenAt);
        Assert.Equal(DateTimeKind.Utc, leader.LastLoginAt!.Value.Kind);
    }

    [Fact]
    public async Task DeletedLeader_KeepsTheirLogEntries()
    {
        await CreateTournamentAsync();

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var leaderId = (await ReadAsync<List<LeaderDto>>(await admin.GetAsync("api/leaders"))).Single().Id;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"api/leaders/{leaderId}")).StatusCode);

        var log = await ReadAsync<ActivityLogPageDto>(await admin.GetAsync($"api/activity?userId={leaderId}"));
        Assert.Equal(2, log.Total); // login and creating the tournament
        Assert.All(log.Entries, e => Assert.Equal(LeaderDisplayName, e.DisplayName));

        var users = await ReadAsync<List<ActivityUserDto>>(await admin.GetAsync("api/activity/users"));
        Assert.DoesNotContain(users, u => u.Id == leaderId);
    }

    [Fact]
    public async Task PlayerUpdate_ShowsBeforeAndAfter_AndCanBeReverted()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Jan", "Jansen", 1500, City: "Delft"));
        await PutAsync<PlayerDto>($"api/players/{player.Id}", new UpdatePlayerRequest("Jan", "Jansen", 1620, City: "Leiden"));

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var update = (await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity"))).Entries
            .Single(e => e.Action == "Players.Update");
        var change = update.Changes.Single(c => c.EntityType == "Player");
        Assert.Equal((ActivityChangeKind.Modified, player.Id.ToString(), "Jan Jansen"), (change.Kind, change.EntityKey, change.Label));
        Assert.Equal(
            [new ActivityPropertyChangeDto("City", "Delft", "Leiden"), new ActivityPropertyChangeDto("Rating", "1500", "1620")],
            change.Properties.OrderBy(p => p.Name).ToList());
        Assert.True(update.CanRevert);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"api/activity/{update.Id}/revert", null)).StatusCode);

        var reverted = (await GetAsync<List<PlayerDto>>("api/players")).Single(p => p.Id == player.Id);
        Assert.Equal((1500, "Delft"), (reverted.Rating, reverted.City));

        var log = (await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity"))).Entries;
        var revert = log.Single(e => e.Action == "Activity.Revert");
        Assert.Equal(update.Id, revert.RevertOfId);
        Assert.Contains(revert.Changes, c => c.EntityType == "Player" && c.Kind == ActivityChangeKind.Modified);
        var original = log.Single(e => e.Id == update.Id);
        Assert.Equal(revert.Id, original.RevertedBy?.EntryId);
        Assert.False(original.CanRevert);

        await AssertErrorAsync(await admin.PostAsync($"api/activity/{update.Id}/revert", null),
            HttpStatusCode.Conflict, ApiErrorCode.AlreadyReverted);
    }

    [Fact]
    public async Task Revert_IsRefused_WhenChangedAgainSince()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Jan", "Jansen", 1500));
        await PutAsync<PlayerDto>($"api/players/{player.Id}", new UpdatePlayerRequest("Jan", "Jansen", 1600));
        await PutAsync<PlayerDto>($"api/players/{player.Id}", new UpdatePlayerRequest("Jan", "Jansen", 1700));

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var first = (await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity"))).Entries
            .Last(e => e.Action == "Players.Update");

        await AssertErrorAsync(await admin.PostAsync($"api/activity/{first.Id}/revert", null),
            HttpStatusCode.Conflict, ApiErrorCode.ChangedSinceAction);
        Assert.Equal(1700, (await GetAsync<List<PlayerDto>>("api/players")).Single(p => p.Id == player.Id).Rating);
    }

    [Fact]
    public async Task Result_CanBeReverted()
    {
        var tournamentId = await CreateTournamentAsync();
        await AddNewPlayersAsync(tournamentId, 2);
        await StartRoundAsync(tournamentId);
        var game = (await GetRoundsAsync(tournamentId)).Single(r => r.IsStarted).Games.Single();
        await SetResultAsync(tournamentId, game.Id, GameResult.Draw);

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var entry = (await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity"))).Entries
            .Single(e => e.Action == "Tournaments.SetResult");
        var change = entry.Changes.Single(c => c.EntityType == "Game" && c.Kind == ActivityChangeKind.Modified);
        Assert.Contains(new ActivityPropertyChangeDto("Result", "NotPlayed", "Draw"), change.Properties);
        Assert.True(entry.CanRevert);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"api/activity/{entry.Id}/revert", null)).StatusCode);
        var after = (await GetRoundsAsync(tournamentId)).Single(r => r.IsStarted).Games.Single();
        Assert.Equal(GameResult.NotPlayed, after.Result);
    }

    [Fact]
    public async Task OnlyTheAdministrator_CanRevert()
    {
        var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Jan", "Jansen", 1500));
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsync("api/activity/1/revert", null)).StatusCode);
        Assert.Equal(1500, (await GetAsync<List<PlayerDto>>("api/players")).Single(p => p.Id == player.Id).Rating);
    }
}

using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class AuthTests : ApiTestBase
{
    [Fact]
    public async Task WithoutLogin_ApiAndHubAreClosed_LoginPageAssetsAreOpen()
    {
        using var anonymous = await CreateClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/tournaments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/settings/ai")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("hubs/tournament/negotiate?negotiateVersion=1", null)).StatusCode);

        // The client app itself is served without login; it shows the login page.
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("tournament")).StatusCode);
    }

    [Fact]
    public async Task Accounts_CanLogIn_UserNameIgnoresCase()
    {
        using var admin = await CreateClientAsync("ADMIN", AdminPassword);
        var me = await ReadAsync<CurrentUserDto>(await admin.GetAsync("api/auth/me"));
        Assert.Equal(("admin", "Systeembeheerder", UserRole.Admin), (me.UserName, me.DisplayName, me.Role));

        var leader = await GetAsync<CurrentUserDto>("api/auth/me");
        Assert.Equal((LeaderUserName, LeaderDisplayName, UserRole.TournamentLeader), (leader.UserName, leader.DisplayName, leader.Role));
    }

    [Theory]
    [InlineData("admin", "verkeerd!")]
    [InlineData("onbekend", "HelloWorld1!")]
    public async Task WrongCredentials_AreRefused(string userName, string password)
    {
        using var client = await CreateClientAsync();
        await AssertErrorAsync(await client.PostAsJsonAsync("api/auth/login", new LoginRequest(userName, password)),
            HttpStatusCode.Unauthorized, ApiErrorCode.InvalidCredentials);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        using var client = await CreateClientAsync(LeaderUserName, LeaderPassword);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/tournaments")).StatusCode);
    }

    [Fact]
    public async Task TournamentLeader_CannotManageLeaders()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("api/leaders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client.PostAsJsonAsync("api/leaders", new CreateLeaderRequest("piet", "Piet", "Wachtwoord1"))).StatusCode);
    }

    [Fact]
    public async Task Admin_ManagesLeaders_WhoCanThenLogIn()
    {
        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);

        var leaders = await ReadAsync<List<LeaderDto>>(await admin.GetAsync("api/leaders"));
        Assert.Equal(LeaderUserName, Assert.Single(leaders).UserName); // the admin account is not in the list

        var piet = await ReadAsync<LeaderDto>(
            await admin.PostAsJsonAsync("api/leaders", new CreateLeaderRequest(" piet ", " Piet Jansen ", "Wachtwoord1")));
        Assert.Equal(("piet", "Piet Jansen"), (piet.UserName, piet.DisplayName));

        using var pietClient = await CreateClientAsync("piet", "Wachtwoord1");
        Assert.Equal(HttpStatusCode.OK, (await pietClient.GetAsync("api/tournaments")).StatusCode);

        // Renaming without a new password keeps the leader logged in.
        var renamed = await ReadAsync<LeaderDto>(
            await admin.PutAsJsonAsync($"api/leaders/{piet.Id}", new UpdateLeaderRequest("pietj", "Piet J.", null)));
        Assert.Equal(("pietj", "Piet J."), (renamed.UserName, renamed.DisplayName));
        Assert.Equal(HttpStatusCode.OK, (await pietClient.GetAsync("api/tournaments")).StatusCode);

        // A new password logs the leader out everywhere.
        await ReadAsync<LeaderDto>(
            await admin.PutAsJsonAsync($"api/leaders/{piet.Id}", new UpdateLeaderRequest("pietj", "Piet J.", "NieuwWachtwoord")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await pietClient.GetAsync("api/tournaments")).StatusCode);
        using var pietAgain = await CreateClientAsync("pietj", "NieuwWachtwoord");

        // Deleting the account logs the leader out right away.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"api/leaders/{piet.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await pietAgain.GetAsync("api/tournaments")).StatusCode);
    }

    [Fact]
    public async Task CreateLeader_Validates()
    {
        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);

        await AssertErrorAsync(await admin.PostAsJsonAsync("api/leaders", new CreateLeaderRequest(LeaderUserName.ToUpperInvariant(), "Ander", "Wachtwoord1")),
            HttpStatusCode.Conflict, ApiErrorCode.UserNameTaken);
        await AssertErrorAsync(await admin.PostAsJsonAsync("api/leaders", new CreateLeaderRequest(" ", "Ander", "Wachtwoord1")),
            HttpStatusCode.BadRequest, ApiErrorCode.UserNameRequired);
        await AssertErrorAsync(await admin.PostAsJsonAsync("api/leaders", new CreateLeaderRequest("ander", " ", "Wachtwoord1")),
            HttpStatusCode.BadRequest, ApiErrorCode.DisplayNameRequired);
        await AssertErrorAsync(await admin.PostAsJsonAsync("api/leaders", new CreateLeaderRequest("ander", "Ander", "kort")),
            HttpStatusCode.BadRequest, ApiErrorCode.PasswordTooShort);
    }

    [Fact]
    public async Task Admin_CannotBeChangedThroughLeadersApi()
    {
        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var adminId = (await ReadAsync<CurrentUserDto>(await admin.GetAsync("api/auth/me"))).Id;

        await AssertErrorAsync(await admin.DeleteAsync($"api/leaders/{adminId}"), HttpStatusCode.NotFound, ApiErrorCode.UserNotFound);
    }

    [Fact]
    public async Task ChangePassword_KeepsThisSession_EndsOtherSessions()
    {
        using var otherSession = await CreateClientAsync(LeaderUserName, LeaderPassword);

        await AssertErrorAsync(await Client.PutAsJsonAsync("api/auth/password", new ChangePasswordRequest("fout", "Sinaasappel2!")),
            HttpStatusCode.BadRequest, ApiErrorCode.CurrentPasswordIncorrect);
        await AssertErrorAsync(await Client.PutAsJsonAsync("api/auth/password", new ChangePasswordRequest(LeaderPassword, "kort")),
            HttpStatusCode.BadRequest, ApiErrorCode.PasswordTooShort);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.PutAsJsonAsync("api/auth/password", new ChangePasswordRequest(LeaderPassword, "Sinaasappel2!"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("api/tournaments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherSession.GetAsync("api/tournaments")).StatusCode);
        using var _ = await CreateClientAsync(LeaderUserName, "Sinaasappel2!");
    }
}

/// <summary>Database creation and updates: a new database only has the administrator, and restarting keeps the data.</summary>
public class DatabaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"chessfixer-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task NewDatabase_OnlyHasTheAdministrator()
    {
        await using var factory = CreateFactory();
        using var admin = await LogInAsAdminAsync(factory);

        Assert.Empty(await ApiTestBase.ReadAsync<List<LeaderDto>>(await admin.GetAsync("api/leaders")));
        Assert.Empty(await ApiTestBase.ReadAsync<List<PlayerDto>>(await admin.GetAsync("api/players?includeDeleted=true")));
        Assert.Empty(await ApiTestBase.ReadAsync<List<TournamentSummaryDto>>(await admin.GetAsync("api/tournaments")));
    }

    [Fact]
    public async Task Restart_KeepsTheData()
    {
        await using (var factory = CreateFactory())
        {
            using var admin = await LogInAsAdminAsync(factory);
            await ApiTestBase.ReadAsync<PlayerDto>(await admin.PostAsJsonAsync("api/players", new CreatePlayerRequest("Magnus", "Carlsen", 2800)));
        }

        await using (var factory = CreateFactory())
        {
            using var admin = await LogInAsAdminAsync(factory);
            var player = Assert.Single(await ApiTestBase.ReadAsync<List<PlayerDto>>(await admin.GetAsync("api/players")));
            Assert.Equal(("Magnus Carlsen", 2800), (player.Name, player.Rating));
        }
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory() =>
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:ChessFixer", $"Data Source={_path};Pooling=False"));

    private static async Task<HttpClient> LogInAsAdminAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        await ApiTestBase.ReadAsync<CurrentUserDto>(await client.PostAsJsonAsync("api/auth/login",
            new LoginRequest(ApiTestBase.AdminUserName, ApiTestBase.AdminPassword)));
        return client;
    }

    public void Dispose() => File.Delete(_path);
}

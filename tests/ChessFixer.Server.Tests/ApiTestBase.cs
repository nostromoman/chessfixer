using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ChessFixer.Server.Tests;

/// <summary>Runs the real server in memory with its own temporary SQLite database per test.</summary>
public abstract class ApiTestBase : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"chessfixer-test-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    protected HttpClient Client { get; private set; } = null!;

    public const string AdminUserName = "admin";
    public const string AdminPassword = "HelloWorld1!";
    public const string LeaderUserName = "leider";
    public const string LeaderDisplayName = "Toernooi Leider";
    public const string LeaderPassword = "Appelsap1!";

    /// <summary>
    /// Starts the server with a new database (which only has the administrator), has the administrator create a
    /// tournament leader and logs <see cref="Client"/> in as that leader.
    /// </summary>
    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ChessFixer", $"Data Source={_databasePath};Pooling=False");
            builder.ConfigureTestServices(ConfigureServices);
        });
        using (var admin = await CreateClientAsync(AdminUserName, AdminPassword))
            await ReadAsync<LeaderDto>(await admin.PostAsJsonAsync("api/leaders",
                new CreateLeaderRequest(LeaderUserName, LeaderDisplayName, LeaderPassword)));
        Client = await CreateClientAsync(LeaderUserName, LeaderPassword);
    }

    /// <summary>A new client (with its own cookies); logged in when a user name is given.</summary>
    protected async Task<HttpClient> CreateClientAsync(string? userName = null, string? password = null)
    {
        var client = _factory.CreateClient();
        if (userName is not null)
            await ReadAsync<CurrentUserDto>(await client.PostAsJsonAsync("api/auth/login", new LoginRequest(userName, password!)));
        return client;
    }

    /// <summary>Override to replace server services, e.g. with fakes.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        File.Delete(_databasePath);
    }

    // ---------- Helpers ----------

    protected async Task<int> CreateTournamentAsync(PairingSystem system = PairingSystem.Swiss)
    {
        var tournament = await PostAsync<TournamentDto>("api/tournaments", new CreateTournamentRequest("Test", TournamentSettingsDto.Default with { PairingSystem = system }));
        return tournament.Id;
    }

    /// <summary>Creates players in the central list and adds them to the tournament. Returns their ids.</summary>
    protected async Task<List<int>> AddNewPlayersAsync(int tournamentId, int count)
    {
        var ids = new List<int>();
        for (var i = 1; i <= count; i++)
        {
            var player = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Speler", $"{i}", 2000 - i * 10));
            await PostAsync<TournamentDto>($"api/tournaments/{tournamentId}/players", new AddTournamentPlayerRequest(player.Id));
            ids.Add(player.Id);
        }
        return ids;
    }

    protected Task<TournamentDto> GetTournamentAsync(int id) => GetAsync<TournamentDto>($"api/tournaments/{id}");
    protected Task<List<RoundDto>> GetRoundsAsync(int id) => GetAsync<List<RoundDto>>($"api/tournaments/{id}/rounds");
    protected Task<List<StandingDto>> GetStandingsAsync(int id) => GetAsync<List<StandingDto>>($"api/tournaments/{id}/standings");
    protected Task<TournamentDto> StartRoundAsync(int id) => PostAsync<TournamentDto>($"api/tournaments/{id}/rounds/start", null);

    protected Task<TournamentDto> SetResultAsync(int tournamentId, int gameId, GameResult result) =>
        PutAsync<TournamentDto>($"api/tournaments/{tournamentId}/games/{gameId}/result", new SetResultRequest(result));

    /// <summary>Enters a result for every game of the active round that has none yet.</summary>
    protected async Task<TournamentDto> FinishActiveRoundAsync(int tournamentId, GameResult result = GameResult.WhiteWins)
    {
        var round = (await GetRoundsAsync(tournamentId)).Last(r => r.IsStarted);
        TournamentDto? tournament = null;
        foreach (var game in round.Games.Where(g => g.Result == GameResult.NotPlayed))
            tournament = await SetResultAsync(tournamentId, game.Id, result);
        return tournament ?? await GetTournamentAsync(tournamentId);
    }

    protected async Task<T> GetAsync<T>(string url) => await ReadAsync<T>(await Client.GetAsync(url));
    protected async Task<T> PostAsync<T>(string url, object? body) => await ReadAsync<T>(await Client.PostAsJsonAsync(url, body));
    protected async Task<T> PutAsync<T>(string url, object body) => await ReadAsync<T>(await Client.PutAsJsonAsync(url, body));

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    protected static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, ApiErrorCode code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(code, error!.Code);
    }
}
